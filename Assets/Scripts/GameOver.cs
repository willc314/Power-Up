using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverMenu : MonoBehaviour
{
    [Tooltip("Name of the gameplay scene to restart.")]
    public string gameplaySceneName = "SampleScene";

    [Tooltip("Name of the title screen scene.")]
    public string titleSceneName = "TitleScreen";

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameplaySceneName);
    }

    public void GoToTitleScreen()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }

    public void QuitGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }
}