using UnityEngine;
using UnityEngine.SceneManagement;
using VisualPinball.Unity;

namespace VisualPinball.Engine.DMD.Unity
{
	internal static class DmdBridgeBootstrapper
	{
		private static DmdBridgeWatcher _watcher;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void StartWatcher()
		{
			if (_watcher != null) {
				return;
			}

			var gameObject = new GameObject("VPE DMD Bridge");
			Object.DontDestroyOnLoad(gameObject);
			_watcher = gameObject.AddComponent<DmdBridgeWatcher>();
		}

		private sealed class DmdBridgeWatcher : MonoBehaviour
		{
			private const float ScanInterval = 0.5f;
			private float _nextScanTime;

			private void OnEnable()
			{
				SceneManager.sceneLoaded += OnSceneLoaded;
				Install();
			}

			private void OnDisable()
			{
				SceneManager.sceneLoaded -= OnSceneLoaded;
			}

			private void Update()
			{
				if (Time.unscaledTime < _nextScanTime) {
					return;
				}

				_nextScanTime = Time.unscaledTime + ScanInterval;
				Install();
			}

			private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
			{
				Install();
			}

			private static void Install()
			{
				foreach (var player in Object.FindObjectsByType<Player>(FindObjectsInactive.Include)) {
					if (player.GetComponent<DmdBridgePlayer>() == null) {
						player.gameObject.AddComponent<DmdBridgePlayer>();
					}
				}
			}
		}
	}
}
