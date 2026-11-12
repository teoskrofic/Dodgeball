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
        [SerializeField] private Button menuButton;

        [Header("Difficulty menu")]
        [SerializeField] private GameObject difficultyPanel;
        [SerializeField] private Button easyButton;
        [SerializeField] private Button normalButton;
        [SerializeField] private Button hardButton;
        [Tooltip("Optional HUD label that shows the chosen difficulty.")]
        [SerializeField] private Text difficultyText;

        [Header("Game mode tabs (built at runtime from the Easy button)")]
        [SerializeField] private Vector2 modeTabSize = new Vector2(330, 80);
        [SerializeField] private float modeTabY = -165f;
        [SerializeField] private Color modeSelectedColor = Color.white;
        [SerializeField] private Color modeUnselectedColor = new Color(0.45f, 0.45f, 0.45f);

        private Button classicTab;
        private Button sideBounceTab;
        private readonly string[] classicDescriptions = new string[3];

        [Header("Look")]
        [SerializeField] private string heart = "♥";
        [SerializeField] private Color readyColor = new Color(0.3f, 0.9f, 0.4f);
        [SerializeField] private Color coolingColor = new Color(0.6f, 0.6f, 0.6f);

        private void Start()
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            GameManager gm = GameManager.Instance;
            if (restartButton != null) restartButton.onClick.AddListener(() => gm.Restart());
            if (menuButton != null) menuButton.onClick.AddListener(() => gm.BackToMenu());

            BuildModeTabs();

            if (easyButton != null) easyButton.onClick.AddListener(() => gm.StartMatch(AIController.Difficulty.Easy));
            if (normalButton != null) normalButton.onClick.AddListener(() => gm.StartMatch(AIController.Difficulty.Normal));
            if (hardButton != null) hardButton.onClick.AddListener(() => gm.StartMatch(AIController.Difficulty.Hard));

            player.OnDamaged += _ => RefreshLives();
            opponent.OnDamaged += _ => RefreshLives();
            gm.OnGameOver += ShowGameOver;
            gm.OnMatchStarted += HandleMatchStarted;

            // The match may already have started (Restart skips the menu).
            if (gm.IsPlaying) HandleMatchStarted();
            else if (difficultyPanel != null) difficultyPanel.SetActive(true);
            RefreshLives();
        }

        private void HandleMatchStarted()
        {
            if (difficultyPanel != null) difficultyPanel.SetActive(false);
            GameManager gm = GameManager.Instance;
            if (difficultyText != null)
            {
                string mode = gm.CurrentMode == GameManager.GameMode.SideBounce ? "SIDE BOUNCE  -  " : "";
                difficultyText.text = mode + gm.CurrentDifficulty.ToString().ToUpper();
            }
        }

        // ---------- Game mode tabs ----------

        /// <summary>Adds CLASSIC / SIDE BOUNCE tabs under the difficulty buttons by cloning the Easy button.</summary>
        private void BuildModeTabs()
        {
            if (difficultyPanel == null || easyButton == null) return;

            Button[] difficultyButtons = { easyButton, normalButton, hardButton };
            for (int i = 0; i < 3; i++)
            {
                Text d = DescriptionOf(difficultyButtons[i]);
                classicDescriptions[i] = d != null ? d.text : "";
            }

            Transform subtitle = difficultyPanel.transform.Find("Subtitle");
            if (subtitle != null && subtitle.TryGetComponent(out Text sub)) sub.text = "Choose mode and difficulty";

            classicTab = MakeModeTab("ClassicTab", "CLASSIC", -modeTabSize.x * 0.5f - 15f);
            sideBounceTab = MakeModeTab("SideBounceTab", "SIDE BOUNCE", modeTabSize.x * 0.5f + 15f);
            classicTab.onClick.AddListener(() => SelectMode(GameManager.GameMode.Classic));
            sideBounceTab.onClick.AddListener(() => SelectMode(GameManager.GameMode.SideBounce));
            SelectMode(GameManager.Instance.SelectedMode);
        }

        private Button MakeModeTab(string name, string label, float x)
        {
            GameObject go = Instantiate(easyButton.gameObject, easyButton.transform.parent);
            go.name = name;
            RectTransform rt = (RectTransform)go.transform;
            rt.anchoredPosition = new Vector2(x, modeTabY);
            rt.sizeDelta = modeTabSize;

            Transform title = go.transform.Find("Title");
            if (title != null)
            {
                RectTransform trt = (RectTransform)title;
                trt.anchoredPosition = Vector2.zero;
                trt.sizeDelta = modeTabSize;
                Text t = title.GetComponent<Text>();
                t.text = label;
                t.fontSize = 40;
            }
            Transform desc = go.transform.Find("Description");
            if (desc != null) desc.gameObject.SetActive(false);

            Button b = go.GetComponent<Button>();
            b.onClick.RemoveAllListeners();
            return b;
        }

        private void SelectMode(GameManager.GameMode mode)
        {
            GameManager gm = GameManager.Instance;
            gm.SelectedMode = mode;
            bool bounce = mode == GameManager.GameMode.SideBounce;

            if (classicTab != null) classicTab.image.color = bounce ? modeUnselectedColor : modeSelectedColor;
            if (sideBounceTab != null) sideBounceTab.image.color = bounce ? modeSelectedColor : modeUnselectedColor;

            // Difficulty buttons explain what each level means in the chosen mode.
            Button[] difficultyButtons = { easyButton, normalButton, hardButton };
            AIController.Difficulty[] levels = { AIController.Difficulty.Easy, AIController.Difficulty.Normal, AIController.Difficulty.Hard };
            for (int i = 0; i < 3; i++)
            {
                Text d = DescriptionOf(difficultyButtons[i]);
                if (d != null) d.text = bounce ? gm.GetSideBounceRules(levels[i]).description : classicDescriptions[i];
            }
        }

        private static Text DescriptionOf(Button b)
        {
            if (b == null) return null;
            Transform t = b.transform.Find("Description");
            return t != null ? t.GetComponent<Text>() : null;
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
