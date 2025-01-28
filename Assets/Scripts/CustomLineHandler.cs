using System;
using UnityEngine;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine.Events;
using UnityEngine.Rendering;

//Controls drawing, highlighting, and manipulation of lines by the stylus using Unity's LineRenderer component
public class CustomLineDrawing : MonoBehaviour
{
    //Stores all drawn lines
    private List<GameObject> _lines = new();
    //Stores current line being drawn
    private LineRenderer _currentLine;
    //Stored widths on current line; adjusts based on stylus pressure
    private List<float> _currentLineWidths = new();

    //Sets line width bounds
    [SerializeField] private float maxLineWidth = 0.01f;
    [SerializeField] private float minLineWidth = 0.0005f;
    //Stores current line material; applied to LineRenderer
    [SerializeField] private Material material;
    //Stores default line color
    [SerializeField] private Color lineColor = Color.red;
    //Indicates when a line is highlighted
    [SerializeField] private float highlightBrightnessFactor = 0.3f;
    //Stores reference to the CustomStylusHandler class
    [SerializeField] private CustomStylusHandler customStylusHandler;
    //Mesh renderer to represent stylus tip visually
    [SerializeField] private MeshRenderer tipIndicator;

    //Sets distance required to highlight a line; based on stylus proximity
    private float highlightThreshold = 0.01f;

    [Tooltip("Event triggered when fading between Passthrough and VR.")]
    //Unity event exposed in inspector; Calls some aditional logic later on
    [SerializeField]
    private UnityEvent onFadeToggled = new();

    //Tracks different interaction states
    private enum AppMode
    {
        Idle,
        Drawing,
        Highlighted,
        Grabbing
    }
    //Defines current interaction mode
    private AppMode _currentMode = AppMode.Idle;

    //colors
    public Color[] colors = {
        Color.red,
        new Color(1f, 0.5f, 0f),
        Color.yellow,
        new Color(0.5f, 1.0f, 0.5f),
        Color.green,
        new Color(0.3f, 1.0f, 0.3f),
        Color.cyan,
        Color.blue,
        new Color(0.2f, 0.8f, 0.7f),
        new Color(0.6f, 0.1f, 0.9f),
        new Color(0.9f, 0.4f, 0.8f),
        Color.white,
        Color.black
    }; // Array of colors to cycle through
    private int colorIndex = 0; // Index to keep track of the current color

    //Ensures spacing between consecutive points on each line
    private const float MinDistanceBetweenLinePoints = 0.0005f;

    //Last line point added to line
    private Vector3 _previousLinePoint;
    //Stores original color of a highlighted line; used for restoring line state
    private Color _cachedColor;
    //Stores currently highlighted line
    private GameObject _highlightedLine;
    //Captures lines position / rotation when a user begins grabbing a line.
    private Vector3 _grabStartPosition;
    private Quaternion _grabStartRotation;
    //Stores original line point locations in line highlighted for manipulation
    private Vector3[] _originalLinePositions;
    //Stores previous total line length
    private float _previousLineLength;

    //Sets original line color on stylus; registers events on object creation
    private void Awake()
    {
        tipIndicator.material.color = lineColor;
        _cachedColor = lineColor;
        customStylusHandler.OnFrontPressed += HandleFrontPressed;
        customStylusHandler.OnFrontReleased += HandleFrontReleased;
        customStylusHandler.OnBackPressed += HandleBackPressed;
        customStylusHandler.OnBackReleased += HandleBackReleased;
        customStylusHandler.OnDocked += HandleDocked;
        customStylusHandler.OnUndocked += HandleDocked;
    }

    //Registers event listeners when object is destroyed
    private void OnDestroy()
    {
        customStylusHandler.OnFrontPressed -= HandleFrontPressed;
        customStylusHandler.OnFrontReleased -= HandleFrontReleased;
        customStylusHandler.OnBackPressed -= HandleBackPressed;
        customStylusHandler.OnBackReleased -= HandleBackReleased;
        customStylusHandler.OnDocked -= HandleDocked;
        customStylusHandler.OnUndocked -= HandleDocked;
    }

    //Creates a new line when the user starts drawing
    private void StartNewLine()
    {
        var lineObject = new GameObject("Line");
        //LineRenderer component is added to the line object
        var lineRenderer = lineObject.AddComponent<LineRenderer>();
        _currentLine = lineRenderer;
        _currentLine.positionCount = 0;
        //Line length set to 'previousLineLength' variable, which is initialized at 0.0f
        _previousLineLength = 0.0f;
        //Line material set to default via 'material' variable, color set to 'lineColor'
        _currentLine.material = new Material(material) { color = lineColor };
        //Sets defined starting line thickness
        _currentLine.startWidth = minLineWidth;
        _currentLine.endWidth = minLineWidth;
        //Ensures line position is based on world coordinates
        _currentLine.useWorldSpace = true;
        //Forces line to always face the camera; makes sure it is visible from any angle
        _currentLine.alignment = LineAlignment.View;
        //Disables shadows on lines; improves performance and visual consistency
        _currentLine.shadowCastingMode = ShadowCastingMode.Off;
        _currentLine.receiveShadows = false;
        //Stores new line in lines list
        _lines.Add(lineObject);
        //Records starting point in the previous line point width
        _previousLinePoint = Vector3.zero;
        //Resets current line widths list to prepare for new line
        _currentLineWidths.Clear();
    }


    //Adds points to the line when the user clicks while moving/draws with the stylus
    private void AddPointToLine(Vector3 position, float pressure)
    {
        //Checkss to ensure minimum line distance is maintained between points
        if (Vector3.Distance(position, _previousLinePoint) < MinDistanceBetweenLinePoints) return;
        //If distance is acceptable, adds new point to the line at the current position
        _previousLinePoint = position;
        //increase the position count by one
        _currentLine.positionCount++;
        //add our new point
        _currentLine.SetPosition(_currentLine.positionCount - 1, position);
        //now get the current width curve
        var curve = _currentLine.widthCurve;

        // Is this the beginning of the line?
        if (_currentLine.positionCount == 1)
        {
            // First point => simply set the first keyframe
            curve.MoveKey(0, new Keyframe(0f, pressure));
        }
        else
        {
            // otherwise get all positions
            var positions = new Vector3[_currentLine.positionCount];
            _currentLine.GetPositions(positions);

            // sum up the distances between positions to obtain the length of the line
            var totalLengthNew = 0f;
            for (var i = 1; i < _currentLine.positionCount; i++)
            {
                totalLengthNew += Vector3.Distance(positions[i - 1], positions[i]);
            }

            // calculate the time factor we have to apply to all already existing keyframes
            var factor = _previousLineLength / totalLengthNew;

            // then store for the next added point
            _previousLineLength = totalLengthNew;

            // now move all existing keys which are currently based on the totalLengthOld to according positions based on the totalLengthNew
            // we can skip the first one as it will stay at 0 always
            var keys = curve.keys;
            for (var i = 1; i < keys.Length; i++)
            {
                var key = keys[i];
                key.time *= factor;

                curve.MoveKey(i, key);
            }

            // add the new last keyframe
            curve.AddKey(1f, Math.Max(pressure * maxLineWidth, minLineWidth));
        }

        // finally write the curve back to the line
        _currentLine.widthCurve = curve;
    }

    /*
     //OLD
    //Adds points to the line when the user clicks while moving/draws with the stylus
    private void AddPointToLine(Vector3 position, float pressure)
    {
        //Checkss to ensure minimum line distance is maintained between points
        if (Vector3.Distance(position, _previousLinePoint) >= MinDistanceBetweenLinePoints) return;
        //If distance is acceptable, adds new point to the line at the current position
        _previousLinePoint = position;
        _currentLine.positionCount++;
        //Adjusts line width at the current point based on stylus pressure
        _currentLineWidths.Add(Math.Max(pressure * maxLineWidth, minLineWidth));
        _currentLine.SetPosition(_currentLine.positionCount - 1, position);

        //Uses animation curve to update the lines width along its length, making it more visually responsive to the user.
        var curve = new AnimationCurve();
        for (var i = 0; i < _currentLineWidths.Count; i++)
        {
            curve.AddKey(i / (float)_currentLineWidths.Count - 1, _currentLineWidths[i]);
        }

        _currentLine.widthCurve = curve;
    }
*/

    //Checks weather the user is currently drawing; if active (Pressure from tip || Middle Button > 0, the user is drawing)
    private void Update()
    {
        var stylus = customStylusHandler.Stylus;
        var analogInput = Mathf.Max(stylus.tip_value, stylus.cluster_middle_value);

        if (analogInput > 0 && CanDraw())
        {
            //Start new line if not currently drawing a line; switch to drawing mode
            if (_currentMode != AppMode.Drawing) StartNewLine();
            _currentMode = AppMode.Drawing;
            AddPointToLine(stylus.inkingPose.position, analogInput);
        }
        else if (_currentMode == AppMode.Drawing)
        {
            //Ends line if user is no longer drawing; switch to idle mode
            _currentMode = AppMode.Idle;
        }

        if (_currentMode != AppMode.Drawing && _currentMode != AppMode.Grabbing)
        {
            //If user is not drawing or grabbing, attempt to highlight the closest line to stylus
            TryHighlightLine();
        }

        if (_currentMode == AppMode.Grabbing)
        {
            //If user is grabbing a line, move it with their input
            MoveHighlightedLine();
        }
    }

    //Tries to highlight the closest line to the stylus (Determined via FindClosestLine)
    //If a line is within the highlight threshhold, its color is changed to the highlight color
    //If no line is within the highlight threshhold, any previously highlighted lines are unhighlighted and the mode is set to idle
    //
    private void TryHighlightLine()
    {
        var stylus = customStylusHandler.Stylus;
        var closestLine = FindClosestLine(stylus.inkingPose.position);

        //if a line is found near the stylus
        if (closestLine)
        {
            //swap highlighted lines if necessary to ensure the closest line is always highlighted
            if (_highlightedLine != closestLine)
            {
                if (_highlightedLine)
                {
                    //Unhighlight any previously highlighted lines before highlighting the new one
                    UnhighlightLine(_highlightedLine);
                }

                //Highlight the new closest line
                HighlightLine(closestLine);
            }

            //Switch to highlighted mode if a line is found near the stylus
            _currentMode = AppMode.Highlighted;
        }
        //If no line is found near the stylus, unhighlight any previously highlighted lines and switch back to idle mode.
        else if (_highlightedLine)
        {
            UnhighlightLine(_highlightedLine);
            _currentMode = AppMode.Idle;
        }
    }

    //Attempts to find the closest line to the stylus based on its tip position
    //Checks the distance of the closest point of each line segment to the stylus tip
    private GameObject FindClosestLine(Vector3 position)
    {
        GameObject closestLine = null;
        var closestDistance = float.MaxValue;

        //For each drawn line
        foreach (var line in _lines)
        {
            var lineRenderer = line.GetComponent<LineRenderer>();
            //Check each segment of the line renderer
            for (var i = 0; i < lineRenderer.positionCount - 1; i++)
            {
                //Find the nearest point on the current segment of the line to the stylus tip.
                var point = FindNearestPointOnLineSegment(lineRenderer.GetPosition(i),
                        lineRenderer.GetPosition(i + 1), position);
                var distance = Vector3.Distance(point, position);
                //If the point is closer than the current closest point and within the set threshold, update the closest line.
                if (!(distance < closestDistance) || !(distance < highlightThreshold)) continue;
                closestDistance = distance;
                closestLine = line;
            }
        }
        return closestLine;
    }

    //Calculates the nearest point on a provided line segment to a given point
    private Vector3 FindNearestPointOnLineSegment(Vector3 segStart, Vector3 segEnd, Vector3 point)
    {
        //segment vector
        var segVec = segEnd - segStart;
        //segment length
        var segLen = segVec.magnitude;
        //segment direction
        var segDir = segVec.normalized;

        //vector from segment start to point
        var pointVec = point - segStart;
        //projection of point vector onto segment direction
        var projLen = Vector3.Dot(pointVec, segDir);
        //clamp projection length to be within the segment bounds
        var clampedLen = Mathf.Clamp(projLen, 0f, segLen);

        //calculate nearest point on segment
        return segStart + segDir * clampedLen;
    }

    // Method to check if the stylus is both active and not docked.
    private bool CanDraw()
    {
        return customStylusHandler.Stylus is { isActive: true, docked: false };
    }

    # region LINE MANIPULATION METHODS

    //If a line is the closest line within the highlight threshhold, this method is called
    //It will change the color of the line to the highlight color to indicate it's being highlighted
    private void HighlightLine(GameObject line)
    {
        _highlightedLine = line;
        var lineRenderer = line.GetComponent<LineRenderer>();
        UpdateLineColor(line);
    }

    //If a line is no longer the closest line within the highlight threshhold, or all lines have left this threshold, this method is called
    //Reverts the color of the line back to its original color
    private void UnhighlightLine(GameObject line)
    {
        var lineRenderer = line.GetComponent<LineRenderer>();
        lineRenderer.material.color = _cachedColor;
        _highlightedLine = null;
    }

    //Deletes the highlighted line from the lines list and destroys it, deleting it from the scene; Sets current mode to idle
    private void DeleteHighlightedLine()
    {
        if (!_highlightedLine) return;
        _lines.Remove(_highlightedLine);
        Destroy(_highlightedLine);
        _highlightedLine = null;
        _currentMode = AppMode.Idle;
    }

    //TODO: Fix Me???
    // Method to start grabbing a highlighted line
    private void StartGrabbingLine()
    {
        if (!_highlightedLine) return;

        _currentMode = AppMode.Grabbing;
        var lineRenderer = _highlightedLine.GetComponent<LineRenderer>();

        // Capture original positions and rotation of the line when grabbed
        _originalLinePositions = new Vector3[lineRenderer.positionCount];
        for (int i = 0; i < lineRenderer.positionCount; i++)
        {
            _originalLinePositions[i] = lineRenderer.GetPosition(i);
        }

        _grabStartPosition = customStylusHandler.Stylus.inkingPose.position;
        _grabStartRotation = customStylusHandler.Stylus.inkingPose.rotation;

        // Optionally, you can also store the original rotation of the line
        var transformComponent = _highlightedLine.transform;
        _grabStartRotation = transformComponent.rotation;
    }

    //TODO: Fix Me???
    // Method to stop grabbing a highlighted line
    private void StopGrabbingLine()
    {
        if (!_highlightedLine) return;

        _currentMode = AppMode.Idle;

        // Reset the original rotation of the line if needed
        var transformComponent = _highlightedLine.transform;
        transformComponent.rotation = _grabStartRotation;
    }

    //If a player grabs a line, this method is called
    //Moves the entire line based on the stylus's position and rotation
    private void MoveHighlightedLine()
    {
       if (!_highlightedLine) return;
       //Set rotation to rotation of stylus offset by the original rotation difference from the grab start.
       var rotation = customStylusHandler.Stylus.inkingPose.rotation * Quaternion.Inverse(_grabStartRotation);
       var lineRenderer = _highlightedLine.GetComponent<LineRenderer>();
       //Array for new line positions
       var newPositions = new Vector3[_originalLinePositions.Length];
       //Calculate the new positions and translating each position relative to the stylus offset by the distance from the grab start.
       for (int i = 0; i < _originalLinePositions.Length; i++)
       {
           newPositions[i] = rotation * (_originalLinePositions[i] - _grabStartPosition) + customStylusHandler.Stylus.inkingPose.position;
       }

       //Update the LineRenderer with the new positions.
       lineRenderer.SetPositions(newPositions);
    }

    // Method to update the line color when called
    private void UpdateLineColor(GameObject line = null)
    {
        if (line is not null && _highlightedLine)
        {
                //Update Grabbed Line Color; enable highlight
                var lineRenderer = line.GetComponent<LineRenderer>();
                _cachedColor = lineRenderer.material.color;
                lineRenderer.material.color = GetBrighterColor(lineRenderer.material.color, highlightBrightnessFactor); // Increase brightness by 30%
        }
        else if (line is not null)
        {
            //Update Grabbed Line Color; disable highlight
            var lineRenderer = line.GetComponent<LineRenderer>();
            lineRenderer.material.color = _cachedColor;
        }
        else
        {

            // Cycle through the colors array
            lineColor = colors[colorIndex];
            tipIndicator.material.color = lineColor;
            colorIndex = (colorIndex + 1) % colors.Length;
        }

        //if current line is present
        if (_currentLine && _currentMode == AppMode.Drawing)
        {
            // Update the material color of the current line
            _currentLine.material.color = lineColor;
        }
    }

    // Helper method to increase the brightness of a color
    private Color GetBrighterColor(Color color, float brightnessFactor)
    {
        // Ensure the brightness factor is within [0, 1] range
        brightnessFactor = Mathf.Clamp(brightnessFactor, 0f, 1f);

        return new Color(
            Mathf.Min(1f, color.r + (1f - color.r) * brightnessFactor),
            Mathf.Min(1f, color.g + (1f - color.g) * brightnessFactor),
            Mathf.Min(1f, color.b + (1f - color.b) * brightnessFactor)
        );
    }

    //Event Handler Methods ↓
    # endregion
    # region EVENT HANDLER METHODS

    //Event handler for Pressing the handle front button
    //Starts grabbing the line when the stylus is pressed.
    private void HandleFrontPressed()
    {
        if (_currentMode == AppMode.Highlighted)
        {
            StartGrabbingLine();
        }
        else
        {
            UpdateLineColor();
        }
    }

    //Event handler for releasing the handle front button
    //Stops grabbing the line when the stylus is released.
    private void HandleFrontReleased()
    {
        if (_currentMode == AppMode.Grabbing)
        {
            StopGrabbingLine();
        }
    }

    //Event handler for pressing the handle back button
    //Deletes the highlighted line(if any) when the stylus back button is pressed.
    private void HandleBackPressed()
    {
        if (_currentMode == AppMode.Highlighted)
        {
            DeleteHighlightedLine();
        }
    }

    //Event handler for releasing the handle back button
    private void HandleBackReleased()
    {
        //placeholder function for potential future use or further functionality
    }

    //Event handler for when the device is docked
    private void HandleDocked()
    {
        _currentMode = AppMode.Idle;
        //placeholder function for potential future use or further functionality
    }

    //Event handler for when the device is undocked
    private void HandleUndocked()
    {
        //placeholder function for potential future use or further functionality
    }

    # endregion
}