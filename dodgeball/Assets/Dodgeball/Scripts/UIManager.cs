using UnityEngine;
using UnityEngine.UI;

namespace Dodgeball
{
    /// <summary>
    /// Shows lives, the player's dodge cooldown, throw charge and the game over screen.
    /// Only reads game state / listens to events; never changes the game itself (except the Restart button).
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        [Header("Game references")]
        [SerializeField] private Health player;
        [SerializeField] private Health opponent;
        [SerializeField] private DodgeAbility playerDodge;
        [SerializeField] private PlayerController playerController;

        [Header("UI elements")]
        [SerializeField] private Text playerLivesText;
        [SerializeField] private Text opponentLivesText;
        [Tooltip("Image with Image Type = Filled.")]
        [SerializeField] private Image dodgeCooldownFill;
        [SerializeField] private Text dodgeLabel;
        [SerializeField] private Image chargeFill;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private Text gameOverText;
        [SerializeField] private Button restartButton;

        [Header("Look")]
        [SerializeField] private string heart = "♥";
        [SerializeField] private Color readyColor = new Color(0.3f, 0.9f, 0.4f);
        [SerializeField] private Color coolingColor = new Color(0.6f, 0.6f, 0.6f);

        private void Start()
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            if (restartButton != null) restartButton.onClick.AddListener(() => GameManager.Instance.Restart());

            player.OnDamaged += _ => RefreshLives();
            opponent.OnDamaged += _ => RefreshLives();
            if (GameManager.Instance != null) GameManager.Instance.OnGameOver += ShowGameOver;
            RefreshLives();
        }

        private void Update()
        {
            if (playerDodge != null && dodgeCooldownFill != null)
            {
                float p = playerDodge.CooldownProgress;
                dodgeCooldownFill.fillAmount = p;
                dodgeCooldownFill.color = p >= 1f ? readyColor : coolingColor;
                if (dodgeLabel != null) dodgeLabel.text = p >= 1f ? "DODGE" : (playerDodge.Cooldown * (1f - p)).ToString("0.0");
            }
            if (playerController != null && chargeFill != null)
                chargeFill.fillAmount = playerController.Charge01;
        }

        private void RefreshLives()
        {
            if (playerLivesText != null) playerLivesText.text = "YOU  " + Hearts(player);
            if (opponentLivesText != null) opponentLivesText.text = Hearts(opponent) + "  AI";
        }

        private string Hearts(Health h)
        {
            string s = "";
            for (int i = 0; i < h.MaxLives; i++) s += i < h.Lives ? heart : "♡";
            return s;
        }

        private void ShowGameOver(bool playerWon)
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(true);
            if (gameOverText != null)
            {
                gameOverText.text = playerWon ? "You Win" : "You Lose";
                gameOverText.color = playerWon ? readyColor : new Color(1f, 0.35f, 0.3f);
            }
        }
    }
}
