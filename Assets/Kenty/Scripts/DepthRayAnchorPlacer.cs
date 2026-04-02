using System;
using System.Collections.Generic;
using Meta.XR;
using UnityEngine;

namespace Kenty
{
    /// <summary>
    /// コントローラーから Ray を飛ばし、Depth API ベースの環境レイキャストで
    /// ヒットした地点に 3D キャラクター付きの Spatial Anchor を設置・保存・読み込みするコンポーネント。
    /// Global Mesh の生成は不要で、EnvironmentRaycastManager（Depth API）を使用する。
    /// シーン内に存在するアンカーは常に 1 つのみ。
    /// </summary>
    public class DepthRayAnchorPlacer : MonoBehaviour
    {
        // [簡略化のためコメントアウト]
        private const string AnchorUuidKey = "DepthRayAnchorUuid";

        [SerializeField]
        [Tooltip("Ray の始点となる Transform（コントローラーの Transform を指定）")]
        private Transform _rayOrigin;

        [SerializeField]
        [Tooltip("Ray の最大距離（メートル）")]
        private float _rayMaxDistance = 100f;

        [SerializeField]
        [Tooltip("Ray の表示色")]
        private Color _rayColor = Color.white;

        // [簡略化のためコメントアウト]
        [Header("入力設定")]
        [SerializeField]
        [Tooltip("アンカー設置に使用するコントローラー")]
        private OVRInput.Controller _controller = OVRInput.Controller.RTouch;

        [SerializeField]
        [Tooltip("アンカー設置に使用するボタン")]
        private OVRInput.Button _placementButton = OVRInput.Button.PrimaryIndexTrigger;

        [Header("アンカー表示設定")]
        [SerializeField]
        [Tooltip("アンカー位置に生成するキャラクタープレハブ")]
        private GameObject _anchorPrefab;

        [SerializeField]
        [Tooltip("レイのヒットポイントに表示するプレビュー用プレハブ")]
        private GameObject _previewPrefab;

        public bool IsPlacementModeActive { get; private set; }

        // EnvironmentRaycastManager への参照（シーンから取得）
        private EnvironmentRaycastManager _raycastManager;

        // プレビュー表示用のインスタンス（事前生成して表示/非表示を切り替える）
        private GameObject _previewInstance;

        // [簡略化のためコメントアウト]
        // 現在シーンに存在するアンカーの GameObject
        private GameObject _currentAnchorObject;

        // 現在のアンカーコンポーネント
        private OVRSpatialAnchor _currentAnchor;

        // 現在のキャラクターの Animator（アニメーション操作用）
        private Animator _currentAnimator;

        // Ray 表示用の LineRenderer
        private LineRenderer _lineRenderer;

        private void Awake()
        {
            // シーン上の EnvironmentRaycastManager を取得する
            _raycastManager = FindFirstObjectByType<EnvironmentRaycastManager>();
            if (_raycastManager is null)
            {
                Debug.LogError("[DepthRayAnchorPlacer] EnvironmentRaycastManager がシーンに見つかりません。");
            }

            _lineRenderer = CreateLineRenderer();
            SetLineRendererVisible(false);

            // プレビュー用インスタンスを事前生成して非表示にしておく
            if (_previewPrefab is not null)
            {
                _previewInstance = Instantiate(_previewPrefab);
                _previewInstance.name = "PlacementPreview";
                _previewInstance.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            // [簡略化のためコメントアウト]
            // DestroyCurrentAnchor();

            if (_previewInstance is not null)
            {
                Destroy(_previewInstance);
            }
        }

        private void Update()
        {
            if (!IsPlacementModeActive)
            {
                return;
            }

            UpdateRaycast();
        }

        /// <summary>
        /// 配置モードの ON/OFF を切り替える。
        /// Button.onClick から呼ぶことを想定。
        /// </summary>
        public void TogglePlacementMode()
        {
            IsPlacementModeActive = !IsPlacementModeActive;
            SetLineRendererVisible(IsPlacementModeActive);

            // 配置モードを OFF にしたらプレビューも非表示にする
            if (!IsPlacementModeActive && _previewInstance is not null)
            {
                _previewInstance.SetActive(false);
            }

        }

        // [簡略化のためコメントアウト]
        /*
        /// <summary>
        /// 現在のアンカーを永続ストレージに保存する。
        /// Button.onClick から呼ぶことを想定。
        /// </summary>
        public async void SaveAnchor()
        {
            // 配置モードが ON の場合は OFF にする
            if (IsPlacementModeActive)
            {
                TogglePlacementMode();
            }

            if (_currentAnchor is null)
            {
                return;
            }

            // アンカーをデバイスに永続化する
            var result = await _currentAnchor.SaveAnchorAsync();
            if (!result.Success)
            {
                Debug.LogWarning("[DepthRayAnchorPlacer] アンカーの保存に失敗しました。");
                return;
            }

            // UUID を PlayerPrefs に保存して次回起動時に復元できるようにする
            string uuid = _currentAnchor.Uuid.ToString();
            PlayerPrefs.SetString(AnchorUuidKey, uuid);
            PlayerPrefs.Save();

            Debug.Log($"[DepthRayAnchorPlacer] アンカーを保存しました。UUID: {uuid}");
        }

        /// <summary>
        /// 保存済みのアンカーを読み込んで復元する。
        /// Button.onClick から呼ぶことを想定。
        /// </summary>
        public async void LoadSavedAnchor()
        {
            // 配置モードが ON の場合は OFF にする
            if (IsPlacementModeActive)
            {
                TogglePlacementMode();
            }

            // PlayerPrefs から保存済みの UUID を取得する
            string uuidString = PlayerPrefs.GetString(AnchorUuidKey, "");
            if (string.IsNullOrEmpty(uuidString) || !Guid.TryParse(uuidString, out Guid uuid))
            {
                return;
            }
            DestroyCurrentAnchor();

            // 保存済みアンカーをデバイスから読み込む
            var uuids = new List<Guid> { uuid };
            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, unboundAnchors);

            if (!loadResult.Success || unboundAnchors.Count == 0)
            {
                Debug.LogWarning("[DepthRayAnchorPlacer] アンカーの読み込みに失敗しました。");
                return;
            }

            OVRSpatialAnchor.UnboundAnchor unboundAnchor = unboundAnchors[0];

            // アンカーをローカライズして現実空間での位置を特定する
            if (!unboundAnchor.Localized)
            {
                bool localized = await unboundAnchor.LocalizeAsync();
                if (!localized)
                {
                    Debug.LogWarning("[DepthRayAnchorPlacer] アンカーのローカライズに失敗しました。");
                    return;
                }
            }

            // ローカライズされた位置・回転にアンカーを復元する
            unboundAnchor.TryGetPose(out Pose pose);

            // 保存時の向きをそのまま再現する
            GameObject anchorObject = CreateAnchorVisual(pose.position, pose.rotation);
            var anchor = anchorObject.AddComponent<OVRSpatialAnchor>();

            // BindTo は AddComponent と同じフレームで呼ぶ必要がある
            // （Start() 実行前にバインドすることで、新規作成ではなく既存アンカーとして扱われる）
            unboundAnchor.BindTo(anchor);

            _currentAnchorObject = anchorObject;
            _currentAnchor = anchor;
            _currentAnimator = anchorObject.GetComponentInChildren<Animator>();

            Debug.Log($"[DepthRayAnchorPlacer] アンカーを復元しました。UUID: {uuid}");
        }
        */

        /// <summary>
        /// 配置モード中の毎フレーム処理。
        /// EnvironmentRaycastManager（Depth API）を使ってレイキャストし、
        /// ヒットポイントにプレビューを表示する。ボタン押下でアンカーを設置する。
        /// </summary>
        private void UpdateRaycast()
        {
            if (_rayOrigin is null || _raycastManager is null)
            {
                return;
            }

            Vector3 origin = _rayOrigin.position;
            Vector3 direction = _rayOrigin.forward;
            var ray = new Ray(origin, direction);

            // Depth API ベースのレイキャストを行う
            // HitPointOccluded の場合も最後の可視点が hit.point に入るためヒット扱いにする
            bool isHit = _raycastManager.Raycast(ray, out EnvironmentRaycastHit hit, _rayMaxDistance)
                         || hit.status == EnvironmentRaycastHitStatus.HitPointOccluded;

            // Ray の表示を更新する（ヒット時は接触点まで、非ヒット時は最大距離まで）
            Vector3 endPoint = isHit ? hit.point : origin + direction * _rayMaxDistance;
            _lineRenderer.SetPosition(0, origin);
            _lineRenderer.SetPosition(1, endPoint);

            // プレビューをヒットポイントに表示する
            if (_previewInstance is not null)
            {
                if (isHit)
                {
                    // カメラの方向（水平方向のみ）を向く回転を計算する
                    Vector3 cameraPosition = Camera.main.transform.position;
                    Vector3 lookDirection = cameraPosition - hit.point;
                    lookDirection.y = 0f;
                    Quaternion rotation = lookDirection.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(lookDirection)
                        : Quaternion.identity;

                    _previewInstance.transform.SetPositionAndRotation(hit.point, rotation);
                    _previewInstance.SetActive(true);
                }
                else
                {
                    _previewInstance.SetActive(false);
                }
            }

            // [簡略化のためコメントアウト]
            /*
            // 指定されたコントローラーのボタンが押された瞬間にアンカーを設置する
            if (isHit && OVRInput.GetDown(_placementButton, _controller))
            {
                PlaceAnchor(hit.point);
            }
            */
        }

        // [簡略化のためコメントアウト]
        /*
        /// <summary>
        /// 指定位置にアンカーを設置する。
        /// キャラクターはカメラ（ユーザー）の方を向いた状態で生成される。
        /// 既存のアンカーは自動的に破棄される（シーン内に 1 つのみ）。
        /// </summary>
        private async void PlaceAnchor(Vector3 position)
        {
            // アンカー設置時はプレビューを非表示にする
            if (_previewInstance is not null)
            {
                _previewInstance.SetActive(false);
            }

            DestroyCurrentAnchor();

            // カメラの方向（水平方向のみ）を向く回転を計算する
            Vector3 cameraPosition = Camera.main.transform.position;
            Vector3 lookDirection = cameraPosition - position;
            lookDirection.y = 0f;
            Quaternion rotation = lookDirection.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(lookDirection)
                : Quaternion.identity;

            // キャラクタープレハブを生成する
            GameObject anchorObject = CreateAnchorVisual(position, rotation);

            // OVRSpatialAnchor を追加して空間アンカーを作成する
            var anchor = anchorObject.AddComponent<OVRSpatialAnchor>();

            _currentAnchorObject = anchorObject;
            _currentAnchor = anchor;
            _currentAnimator = anchorObject.GetComponentInChildren<Animator>();

            // アンカーの作成とローカライズが完了するまで待機する
            bool localized = await anchor.WhenLocalizedAsync();
            if (!localized)
            {
                Debug.LogWarning("[DepthRayAnchorPlacer] アンカーのローカライズに失敗しました。");
                return;
            }

            Debug.Log($"[DepthRayAnchorPlacer] アンカーを設置しました。位置: {position}");
        }

        /// <summary>
        /// アンカー位置にキャラクタープレハブを生成する。
        /// </summary>
        private GameObject CreateAnchorVisual(Vector3 position, Quaternion rotation)
        {
            var anchorObject = Instantiate(_anchorPrefab, position, rotation);
            anchorObject.name = "DepthRayAnchor";
            return anchorObject;
        }

        /// <summary>
        /// 現在のアンカー GameObject を破棄する。
        /// </summary>
        private void DestroyCurrentAnchor()
        {
            if (_currentAnchorObject is not null)
            {
                Destroy(_currentAnchorObject);
                _currentAnchorObject = null;
                _currentAnchor = null;
                _currentAnimator = null;
            }
        }

        /// <summary>
        /// キャラクターのアニメーションを次に送る。
        /// Button.onClick から呼ぶことを想定。
        /// </summary>
        public void AnimationNext()
        {
            if (_currentAnimator is not null)
            {
                _currentAnimator.SetTrigger("Next");
            }
        }

        /// <summary>
        /// キャラクターのアニメーションを前に戻す。
        /// Button.onClick から呼ぶことを想定。
        /// </summary>
        public void AnimationBack()
        {
            if (_currentAnimator is not null)
            {
                _currentAnimator.SetTrigger("Back");
            }
        }
        */

        /// <summary>
        /// Ray 表示用の LineRenderer をランタイムで生成する。
        /// </summary>
        private LineRenderer CreateLineRenderer()
        {
            var lr = gameObject.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.005f;
            lr.endWidth = 0.005f;
            lr.material = new Material(Shader.Find("Unlit/Color"))
            {
                color = _rayColor
            };
            lr.useWorldSpace = true;
            return lr;
        }

        /// <summary>
        /// LineRenderer の表示・非表示を切り替える。
        /// </summary>
        private void SetLineRendererVisible(bool visible)
        {
            _lineRenderer.enabled = visible;
        }
    }
}
