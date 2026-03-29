using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Kenty
{
    /// <summary>
    /// コントローラーから Ray を飛ばし、Global Mesh との接触点に
    /// Spatial Anchor を設置・保存・読み込みするコンポーネント。
    /// シーン内に存在するアンカーは常に 1 つのみ。
    /// </summary>
    public class SpatialAnchorPlacer : MonoBehaviour
    {
        private const string AnchorUuidKey = "SpatialAnchorUuid";

        [Header("Ray 設定")]
        [SerializeField]
        [Tooltip("Ray の始点となる Transform（コントローラーの Transform を指定）")]
        private Transform _rayOrigin;

        [SerializeField]
        [Tooltip("Ray の最大距離（メートル）")]
        private float _rayMaxDistance = 30f;

        [SerializeField]
        [Tooltip("Raycast の対象レイヤー")]
        private LayerMask _rayLayerMask = ~0;

        [SerializeField]
        [Tooltip("Ray の表示色")]
        private Color _rayColor = Color.white;

        [Header("入力設定")]
        [SerializeField]
        [Tooltip("アンカー設置に使用するコントローラー")]
        private OVRInput.Controller _controller = OVRInput.Controller.RTouch;

        [SerializeField]
        [Tooltip("アンカー設置に使用するボタン")]
        private OVRInput.Button _placementButton = OVRInput.Button.PrimaryIndexTrigger;

        [Header("アンカー表示設定")]
        [SerializeField]
        [Tooltip("アンカーの表示サイズ（メートル）")]
        private float _anchorScale = 0.05f;

        [SerializeField]
        [Tooltip("アンカーの表示色")]
        private Color _anchorColor = new(1f, 0.3f, 0.3f, 1f);

        [Header("イベント")]
        [SerializeField]
        [Tooltip("配置モードが切り替わったときに発火するイベント")]
        private UnityEvent<bool> _onPlacementModeChanged = new();

        [SerializeField]
        [Tooltip("ステータスメッセージが更新されたときに発火するイベント")]
        private UnityEvent<string> _onStatusChanged = new();

        /// <summary>
        /// 配置モードが切り替わったときに発火するイベント
        /// </summary>
        public UnityEvent<bool> OnPlacementModeChanged => _onPlacementModeChanged;

        /// <summary>
        /// ステータスメッセージが更新されたときに発火するイベント
        /// </summary>
        public UnityEvent<string> OnStatusChanged => _onStatusChanged;

        /// <summary>
        /// 配置モードが有効かどうか
        /// </summary>
        public bool IsPlacementModeActive { get; private set; }

        // 現在シーンに存在するアンカーの GameObject
        private GameObject _currentAnchorObject;

        // 現在のアンカーコンポーネント
        private OVRSpatialAnchor _currentAnchor;

        // Ray 表示用の LineRenderer
        private LineRenderer _lineRenderer;

        // アンカー表示用のマテリアル
        private Material _anchorMaterial;

        private void Awake()
        {
            _lineRenderer = CreateLineRenderer();
            _anchorMaterial = CreateAnchorMaterial();
            SetLineRendererVisible(false);
        }

        private void OnDestroy()
        {
            DestroyCurrentAnchor();

            if (_anchorMaterial is not null)
            {
                Destroy(_anchorMaterial);
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
            _onPlacementModeChanged?.Invoke(IsPlacementModeActive);

            string message = IsPlacementModeActive ? "Placement: ON" : "Placement: OFF";
            SetStatus(message);
        }

        /// <summary>
        /// 現在のアンカーを永続ストレージに保存する。
        /// Button.onClick から呼ぶことを想定。
        /// </summary>
        public async void SaveAnchor()
        {
            if (_currentAnchor is null)
            {
                SetStatus("No Anchor to Save");
                return;
            }

            SetStatus("Saving ...");

            // アンカーをデバイスに永続化する
            var result = await _currentAnchor.SaveAnchorAsync();
            if (!result.Success)
            {
                SetStatus("Save Failed");
                Debug.LogWarning("[SpatialAnchorPlacer] アンカーの保存に失敗しました。");
                return;
            }

            // UUID を PlayerPrefs に保存して次回起動時に復元できるようにする
            string uuid = _currentAnchor.Uuid.ToString();
            PlayerPrefs.SetString(AnchorUuidKey, uuid);
            PlayerPrefs.Save();

            SetStatus("Anchor Saved");
            Debug.Log($"[SpatialAnchorPlacer] アンカーを保存しました。UUID: {uuid}");
        }

        /// <summary>
        /// 保存済みのアンカーを読み込んで復元する。
        /// Button.onClick から呼ぶことを想定。
        /// </summary>
        public async void LoadSavedAnchor()
        {
            // PlayerPrefs から保存済みの UUID を取得する
            string uuidString = PlayerPrefs.GetString(AnchorUuidKey, "");
            if (string.IsNullOrEmpty(uuidString) || !Guid.TryParse(uuidString, out Guid uuid))
            {
                SetStatus("No Saved Anchor");
                return;
            }

            SetStatus("Loading ...");
            DestroyCurrentAnchor();

            // 保存済みアンカーをデバイスから読み込む
            var uuids = new List<Guid> { uuid };
            var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>();
            var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, unboundAnchors);

            if (!loadResult.Success || unboundAnchors.Count == 0)
            {
                SetStatus("Load Failed");
                Debug.LogWarning("[SpatialAnchorPlacer] アンカーの読み込みに失敗しました。");
                return;
            }

            OVRSpatialAnchor.UnboundAnchor unboundAnchor = unboundAnchors[0];

            // アンカーをローカライズして現実空間での位置を特定する
            if (!unboundAnchor.Localized)
            {
                bool localized = await unboundAnchor.LocalizeAsync();
                if (!localized)
                {
                    SetStatus("Localize Failed");
                    Debug.LogWarning("[SpatialAnchorPlacer] アンカーのローカライズに失敗しました。");
                    return;
                }
            }

            // ローカライズされた位置にアンカーを復元する
            unboundAnchor.TryGetPose(out Pose pose);

            GameObject anchorObject = CreateAnchorVisual(pose.position);
            var anchor = anchorObject.AddComponent<OVRSpatialAnchor>();

            // BindTo は AddComponent と同じフレームで呼ぶ必要がある
            // （Start() 実行前にバインドすることで、新規作成ではなく既存アンカーとして扱われる）
            unboundAnchor.BindTo(anchor);

            _currentAnchorObject = anchorObject;
            _currentAnchor = anchor;

            SetStatus("Anchor Loaded");
            Debug.Log($"[SpatialAnchorPlacer] アンカーを復元しました。UUID: {uuid}");
        }

        /// <summary>
        /// 配置モード中の毎フレーム処理。
        /// Ray を飛ばして可視化し、トリガー押下でアンカーを設置する。
        /// </summary>
        private void UpdateRaycast()
        {
            if (_rayOrigin is null)
            {
                return;
            }

            Vector3 origin = _rayOrigin.position;
            Vector3 direction = _rayOrigin.forward;

            // GlobalMesh の MeshCollider に対して Raycast を行う
            bool isHit = Physics.Raycast(origin, direction, out RaycastHit hit, _rayMaxDistance, _rayLayerMask);

            // Ray の表示を更新する（ヒット時は接触点まで、非ヒット時は最大距離まで）
            Vector3 endPoint = isHit ? hit.point : origin + direction * _rayMaxDistance;
            _lineRenderer.SetPosition(0, origin);
            _lineRenderer.SetPosition(1, endPoint);

            // 指定されたコントローラーのボタンが押された瞬間にアンカーを設置する
            if (isHit && OVRInput.GetDown(_placementButton, _controller))
            {
                PlaceAnchor(hit.point);
            }
        }

        /// <summary>
        /// 指定位置にアンカーを設置する。
        /// 既存のアンカーは自動的に破棄される（シーン内に 1 つのみ）。
        /// </summary>
        private async void PlaceAnchor(Vector3 position)
        {
            DestroyCurrentAnchor();

            // アンカーの見た目（Sphere）を生成する
            GameObject anchorObject = CreateAnchorVisual(position);

            // OVRSpatialAnchor を追加して空間アンカーを作成する
            var anchor = anchorObject.AddComponent<OVRSpatialAnchor>();

            _currentAnchorObject = anchorObject;
            _currentAnchor = anchor;

            // アンカーの作成とローカライズが完了するまで待機する
            bool localized = await anchor.WhenLocalizedAsync();
            if (!localized)
            {
                Debug.LogWarning("[SpatialAnchorPlacer] アンカーのローカライズに失敗しました。");
                SetStatus("Anchor Failed");
                return;
            }

            SetStatus("Anchor Placed");
            Debug.Log($"[SpatialAnchorPlacer] アンカーを設置しました。位置: {position}");
        }

        /// <summary>
        /// アンカーの見た目となる Sphere を生成する。
        /// Raycast に干渉しないよう SphereCollider は削除する。
        /// </summary>
        private GameObject CreateAnchorVisual(Vector3 position)
        {
            var anchorObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            anchorObject.name = "SpatialAnchor";
            anchorObject.transform.position = position;
            anchorObject.transform.localScale = Vector3.one * _anchorScale;

            // Raycast に干渉しないようコライダーを削除する
            var collider = anchorObject.GetComponent<SphereCollider>();
            if (collider is not null)
            {
                Destroy(collider);
            }

            // マテリアルを設定する
            var renderer = anchorObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _anchorMaterial;

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
            }
        }

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

        /// <summary>
        /// アンカー表示用のマテリアルをランタイムで生成する。
        /// </summary>
        private Material CreateAnchorMaterial()
        {
            return new Material(Shader.Find("Standard"))
            {
                color = _anchorColor
            };
        }

        /// <summary>
        /// ステータスメッセージを更新し、イベントを発火する。
        /// </summary>
        private void SetStatus(string message)
        {
            _onStatusChanged?.Invoke(message);
        }
    }
}
