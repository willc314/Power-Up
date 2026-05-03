using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor menu shortcuts for clearing persistent high-score state without
/// touching player-facing settings (FPS, resolution, fullscreen).
/// </summary>
public static class HighScoreResetMenu
{
    [MenuItem("Tools/Power-Up/Reset High Score")]
    public static void ResetHighScore()
    {
        int previous = PlayerPrefs.GetInt(GameManager.PrefsHighScore, 0);
        PlayerPrefs.DeleteKey(GameManager.PrefsHighScore);
        PlayerPrefs.DeleteKey(GameManager.PrefsLastScore);
        PlayerPrefs.DeleteKey(GameManager.PrefsWasNewHigh);
        PlayerPrefs.Save();
        Debug.Log($"[Power-Up] High score reset (was {previous}). Settings (FPS / resolution / fullscreen) untouched.");
    }
}
