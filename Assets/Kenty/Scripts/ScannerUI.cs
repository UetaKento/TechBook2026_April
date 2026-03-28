using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kenty
{
    /// <summary>
    /// World Space Canvas 上のボタンとテキストを管理し、
    /// GlobalMeshScanner のスキャン操作とステータス表示を行うコンポーネント。
    /// </summary>
    public class ScannerUI : MonoBehaviour
    {
        [Header("UI参照")]
        [SerializeField]
        [Tooltip("スキャン開始ボタン")]
        private Button _scanButton;

        [SerializeField]
        [Tooltip("ステータス表示テキスト")]
        private TextMeshProUGUI _statusText;

        [Header("スキャナー参照")]
        [SerializeField]
        [Tooltip("GlobalMeshScanner コンポーネントへの参照")]
        private GlobalMeshScanner _scanner;

        private void Start()
        {
            // ボタン押下時にスキャンを開始する
            _scanButton.onClick.AddListener(OnScanButtonClicked);

            // スキャン状態の変化を監視する
            _scanner.OnScanStateChanged.AddListener(OnScanStateChanged);

            // 初期状態の表示を設定する
            UpdateUI(GlobalMeshScanner.ScanState.Idle);
        }

        private void OnDestroy()
        {
            _scanButton.onClick.RemoveListener(OnScanButtonClicked);

            if (_scanner is not null)
            {
                _scanner.OnScanStateChanged.RemoveListener(OnScanStateChanged);
            }
        }

        /// <summary>
        /// スキャンボタンが押されたときの処理。
        /// </summary>
        private void OnScanButtonClicked()
        {
            _scanner.StartScan();
        }

        /// <summary>
        /// スキャン状態が変化したときの処理。
        /// </summary>
        private void OnScanStateChanged(GlobalMeshScanner.ScanState state)
        {
            UpdateUI(state);
        }

        /// <summary>
        /// 状態に応じてボタンとテキストの表示を更新する。
        /// </summary>
        private void UpdateUI(GlobalMeshScanner.ScanState state)
        {
            switch (state)
            {
                case GlobalMeshScanner.ScanState.Idle:
                    _statusText.text = "Scan Start";
                    _scanButton.interactable = true;
                    break;

                case GlobalMeshScanner.ScanState.Scanning:
                    _statusText.text = "Scaning ...";
                    _scanButton.interactable = false;
                    break;

                case GlobalMeshScanner.ScanState.Completed:
                    _statusText.text = "Scan Complete";
                    _scanButton.interactable = true;
                    break;

                case GlobalMeshScanner.ScanState.Failed:
                    _statusText.text = "Scan Failed";
                    _scanButton.interactable = true;
                    break;
            }
        }
    }
}
