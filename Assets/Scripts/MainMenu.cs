using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Tooltip("Name of the gameplay scene to load.")]
    public string gameplaySceneName = "SampleScene";

    private void Start()
    {
        // Start the title music if the scene has a MusicManager set up with a title clip.
        if (MusicManager.Instance != null) MusicManager.Instance.PlayTitle();
    }

    public void StartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    /// <summary>
    /// Wire your "Options" UI Button's OnClick to this method. It opens the
    /// OptionsMenu singleton (created automatically when an OptionsMenu
    /// component is present in the scene).
    /// </summary>
    public void OpenOptions()
    {
        if (OptionsMenu.Instance != null)
            OptionsMenu.Instance.Show();
        else
            Debug.LogWarning("MainMenu.OpenOptions: no OptionsMenu in the scene. Add an empty GameObject with the OptionsMenu component.");
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}