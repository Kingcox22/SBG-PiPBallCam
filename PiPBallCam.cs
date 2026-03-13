using BepInEx;
using UnityEngine;
using UnityEngine.UI; 
using System.Linq;
using System;
using System.Reflection;

[BepInPlugin("com.kingcox.sbg.pipballcam", "SBG PiP Ball Cam", "1.1.4")]
public class BallCamMod : BaseUnityPlugin
{
    private Camera _ballCam;
    private GameObject _ballCamObj;
    
    private float _smoothSpeed = 10f; 
    private float _movementThreshold = 0.02f; 
    private float _lingerTime = 1.5f;        
    private float _lastMoveTime;
    private string _currentTargetName = "";

    private Vector3 _lastBallPos;
    private Vector3 _stableDirection = Vector3.back;
    
    // Fix for CS0103: Declare the style field here
    private GUIStyle _labelStyle;

    // --- Initialization ---

    void Awake()
    {
        // Initialize the style so it's ready for OnGUI
        _labelStyle = new GUIStyle();
        _labelStyle.alignment = TextAnchor.UpperCenter;
        _labelStyle.normal.textColor = Color.white;
        _labelStyle.fontStyle = FontStyle.Bold;
        _labelStyle.fontSize = 16;
    }

    // --- Reflection Helpers ---
    
    private string GetSafeName(PlayerInfo info)
    {
        if (info == null) return "Unknown";
        Type t = info.GetType();

        var prop = t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        if (prop != null) return prop.GetValue(info)?.ToString() ?? "Unknown";

        var field = t.GetField("playerName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null) return field.GetValue(info)?.ToString() ?? "Unknown";

        Component idComp = info.GetComponent("PlayerId");
        if (idComp != null)
        {
            Type idType = idComp.GetType();
            string[] names = { "_playerName", "networkedPlayerName", "playerName" };
            foreach (var n in names)
            {
                var f = idType.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(idComp)?.ToString() ?? "Unknown";
                var p = idType.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return p.GetValue(idComp)?.ToString() ?? "Unknown";
            }
        }
        return "Golfer";
    }

    private PlayerGolfer GetAsGolfer(PlayerInfo info)
    {
        if (info == null) return null;
        var prop = info.GetType().GetProperty("AsGolfer", BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(info) as PlayerGolfer;
    }

    private object GetAsSpectator(PlayerInfo info)
    {
        if (info == null) return null;
        var prop = info.GetType().GetProperty("AsSpectator", BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(info);
    }

    private Vector3 GetBallVelocity(Rigidbody rb)
    {
        if (rb == null) return Vector3.zero;
        PropertyInfo prop = rb.GetType().GetProperty("linearVelocity") ?? rb.GetType().GetProperty("velocity");
        if (prop != null) return (Vector3)prop.GetValue(rb);
        return Vector3.zero;
    }

    // --- Main Logic ---

    void Update()
    {
        if (Camera.main == null || GameManager.LocalPlayerInfo == null)
        {
            ClosePip();
            return;
        }

        // Get the appropriate ball based on our current state
        GolfBall targetBall = DetermineTargetBall();

        if (targetBall != null)
        {
            // Assuming GolfBall has a Rigidbody property based on your original code
            float speed = GetBallVelocity(targetBall.Rigidbody).magnitude;

            if (speed > _movementThreshold || (Time.time - _lastMoveTime < _lingerTime))
            {
                if (speed > _movementThreshold) _lastMoveTime = Time.time;
                ShowPip(targetBall);
            }
            else
            {
                ClosePip();
                _lastBallPos = Vector3.zero; 
            }
        }
        else
        {
            ClosePip();
        }
    }

    // Completely replaces GetActualSpectatorTarget()
    private GolfBall DetermineTargetBall()
    {
        var lp = GameManager.LocalPlayerInfo;
        if (lp == null) return null;

        // Directly access the component we reverse-engineered
        var spectator = lp.GetComponent<PlayerSpectator>();

        // 1. Check if we are actively spectating
        if (spectator != null && spectator.IsSpectating)
        {
            // 2. Check if the game is making us spectate a specific loose ball
            if (spectator.TargetBall != null)
            {
                _currentTargetName = "Spectated Ball";
                return spectator.TargetBall;
            }

            // 3. Check if we are spectating a specific player
            if (spectator.TargetPlayer != null)
            {
                _currentTargetName = GetSafeName(spectator.TargetPlayer);
                var spectatedGolfer = GetAsGolfer(spectator.TargetPlayer);
                return spectatedGolfer?.OwnBall;
            }
        }

        // 4. Fallback: We are not spectating, so track our own ball
        _currentTargetName = GetSafeName(lp);
        var localGolfer = GetAsGolfer(lp);
        return localGolfer?.OwnBall;
    }

    private void ShowPip(GolfBall ball)
    {
        if (_ballCamObj == null) CreateBallCamera();
        if (!_ballCamObj.activeSelf) _ballCamObj.SetActive(true);
        if (_ballCam != null) _ballCam.enabled = true;

        Vector3 currentBallPos = ball.transform.position;

        if (_lastBallPos != Vector3.zero)
        {
            Vector3 movementDelta = currentBallPos - _lastBallPos;
            Vector3 horizontalDelta = new Vector3(movementDelta.x, 0, movementDelta.z);

            if (horizontalDelta.magnitude > 0.01f)
            {
                _stableDirection = Vector3.Slerp(_stableDirection, horizontalDelta.normalized, Time.deltaTime * 5f);
            }
        }
        _lastBallPos = currentBallPos;

        Vector3 targetCamPos = currentBallPos - (_stableDirection * 3.0f) + (Vector3.up * 1.2f);
        _ballCam.transform.position = Vector3.Lerp(_ballCam.transform.position, targetCamPos, Time.deltaTime * _smoothSpeed);
        _ballCam.transform.LookAt(currentBallPos + Vector3.up * 0.15f);
    }

    private void ClosePip()
    {
        if (_ballCamObj != null)
        {
            _ballCamObj.SetActive(false);
            if (_ballCam != null) _ballCam.enabled = false;
        }
        _currentTargetName = ""; 
    }

    private void OnGUI()
    {
        // 1. Visibility checks
        if (_ballCamObj == null || !_ballCamObj.activeInHierarchy || _ballCam == null || !_ballCam.enabled)
        {
            return;
        }

        if (string.IsNullOrEmpty(_currentTargetName)) return;
        if (_labelStyle == null) return; // Extra safety check

        // 2. Position and Style Setup
        Rect r = _ballCam.pixelRect;
        float guiX = r.x;
        float guiY = (Screen.height - r.yMax) + 5f; 

        Rect mainRect = new Rect(guiX, guiY, r.width, 5f);
        string mainText = $"<b>{_currentTargetName}</b>";

        // Create a temporary style for the outline that is identical but black
        GUIStyle outlineStyle = new GUIStyle(_labelStyle);
        outlineStyle.normal.textColor = Color.black;

        // 3. Draw the Outline (4 directions)
        // Adjust the offset if you want a thicker outline.
        float offset = 2f;

        // Up-Left
        GUI.Label(new Rect(mainRect.x - offset, mainRect.y - offset, mainRect.width, mainRect.height), mainText, outlineStyle);
        // Up-Right
        GUI.Label(new Rect(mainRect.x + offset, mainRect.y - offset, mainRect.width, mainRect.height), mainText, outlineStyle);
        // Down-Left
        GUI.Label(new Rect(mainRect.x - offset, mainRect.y + offset, mainRect.width, mainRect.height), mainText, outlineStyle);
        // Down-Right
        GUI.Label(new Rect(mainRect.x + offset, mainRect.y + offset, mainRect.width, mainRect.height), mainText, outlineStyle);

        // 4. Draw the Main White Text
        GUI.Label(mainRect, mainText, _labelStyle);
    }

    private void CreateBallCamera()
    {
        _ballCamObj = new GameObject("PiPBallCamera");
        _ballCam = _ballCamObj.AddComponent<Camera>();
		_ballCam.tag = "No Grass";

		// Rect positioning: x=0.79 (Right), y=0.22 (Raised)
		float w = 0.20f;
        float h = 0.20f;
        float x = 0.79f; 
        float y = 0.25f; 

        _ballCam.rect = new Rect(x, y, w, h); 
        _ballCam.depth = 999; 
        _ballCam.cullingMask = ~(1 << 5); 
        DontDestroyOnLoad(_ballCamObj);
    }
}