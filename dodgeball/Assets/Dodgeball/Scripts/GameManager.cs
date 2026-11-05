using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dodgeball
{
    /// <summary>
    /// Starts the match (spawns balls, places characters), listens for deaths and ends the game.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Health player;
        [SerializeField] private Health opponent;
        [SerializeField] private Ball ballPrefab;

        [Header("Rules")]
        [Range(1, 3)] [SerializeField] private int ballCount = 3;
        [SerializeField] private AIController.Difficulty aiDifficulty = AIController.Difficulty.Normal;

        [Header("Physics layers (created by the scene builder)")]
        [SerializeField] private string ballLayer = "Ball";
        [SerializeField] private string dividerLayer = "Divider";

        public static GameManager Instance { get; private set; }
        public bool IsGameOver { get; private set; }

        /// <summary>true = player won.</summary>
        public event Action<bool> OnGameOver;

        private void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;

            // Balls fly over the center divider; only characters are blocked by it.
            int ball = LayerMask.NameToLayer(ballLayer);
            int divider = LayerMask.NameToLayer(dividerLayer);
            if (ball >= 0 && divider >= 0) Physics.IgnoreLayerCollision(ball, divider, true);
        }

        private void Start()
        {
            ArenaBounds arena = ArenaBounds.Instance;
            if (arena != null)
            {
                player.GetComponent<CharacterMotor>().Teleport(arena.SpawnPoint(-1) + Vector3.up * 1.1f);
                opponent.GetComponent<CharacterMotor>().Teleport(arena.SpawnPoint(1) + Vector3.up * 1.1f);
            }

            AIController ai = opponent.GetComponent<AIController>();
            if (ai != null) ai.SetDifficulty(aiDifficulty);

            SpawnBalls();
            player.OnDied += HandleDeath;
            opponent.OnDied += HandleDeath;
        }

        private void SpawnBalls()
        {
            if (ballPrefab == null || ArenaBounds.Instance == null) return;
            for (int i = 0; i < ballCount; i++)
            {
                // Spread the balls evenly along the center line.
                float t = ballCount == 1 ? 0f : Mathf.Lerp(-0.6f, 0.6f, i / (float)(ballCount - 1));
                Ball ball = Instantiate(ballPrefab);
                ball.Respawn(ArenaBounds.Instance.CenterLinePoint(t));
            }
        }

        private void HandleDeath(Health dead)
        {
            if (IsGameOver) return;
            IsGameOver = true;

            // Freeze both characters.
            foreach (Health h in new[] { player, opponent })
            {
                foreach (MonoBehaviour b in h.GetComponents<MonoBehaviour>())
                    if (b is PlayerController || b is AIController) b.enabled = false;
            }
            OnGameOver?.Invoke(dead == opponent);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().buildIndex : 0);
        }
    }
}
