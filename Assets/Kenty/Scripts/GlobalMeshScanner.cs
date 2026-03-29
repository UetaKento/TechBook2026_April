using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace Kenty
{
    /// <summary>
    /// Quest 3 の Global Mesh（空間メッシュ）をスキャンし、
    /// 可視化および物理コリジョンとして生成するコンポーネント。
    /// </summary>
    public class GlobalMeshScanner : MonoBehaviour
    {
        /// <summary>
        /// スキャンの状態を表す列挙型
        /// </summary>
        public enum ScanState
        {
            /// <summary>待機中</summary>
            Idle,
            /// <summary>スキャン中</summary>
            Scanning,
            /// <summary>スキャン完了</summary>
            Completed,
            /// <summary>スキャン失敗</summary>
            Failed
        }

        [Header("メッシュの見た目設定")]
        [SerializeField]
        [Tooltip("メッシュに適用するマテリアル（未設定の場合はデフォルトマテリアルを使用）")]
        private Material _meshMaterial;

        [SerializeField]
        [Tooltip("生成するメッシュに設定するレイヤー")]
        private int _meshLayer;

        [Header("イベント")]
        [SerializeField]
        [Tooltip("スキャン状態が変化したときに発火するイベント")]
        private UnityEvent<ScanState> _onScanStateChanged = new();

        [SerializeField]
        [Tooltip("ステータスメッセージが更新されたときに発火するイベント")]
        private UnityEvent<string> _onStatusChanged = new();

        /// <summary>
        /// スキャン状態が変化したときに発火するイベント
        /// </summary>
        public UnityEvent<ScanState> OnScanStateChanged => _onScanStateChanged;

        /// <summary>
        /// ステータスメッセージが更新されたときに発火するイベント。
        /// Inspector で TextMeshProUGUI.set_text に直接紐付けて使う。
        /// </summary>
        public UnityEvent<string> OnStatusChanged => _onStatusChanged;

        /// <summary>
        /// 現在のスキャン状態
        /// </summary>
        public ScanState CurrentState { get; private set; } = ScanState.Idle;

        // 生成したメッシュ GameObject を管理するリスト
        private readonly List<GameObject> _meshObjects = new();

        private void OnDestroy()
        {
            ClearMeshObjects();
        }

        /// <summary>
        /// スキャンを開始する。
        /// 前回のメッシュを削除してから、ルームスキャンを実行する。
        /// </summary>
        public async void StartScan()
        {
            if (CurrentState == ScanState.Scanning)
            {
                return;
            }

            SetState(ScanState.Scanning);
            ClearMeshObjects();

            bool success = await ScanAsync();
            SetState(success ? ScanState.Completed : ScanState.Failed);
        }

        /// <summary>
        /// スキャン処理の本体。
        /// ルームスキャンをリクエストし、Global Mesh を取得して Unity Mesh に変換する。
        /// </summary>
        private async Task<bool> ScanAsync()
        {
            // ルームスキャンUIを表示してユーザーにスキャンしてもらう
            bool sceneCaptured = await OVRScene.RequestSpaceSetup();
            if (!sceneCaptured)
            {
                Debug.LogWarning("[GlobalMeshScanner] ルームスキャンがキャンセルまたは失敗しました。");
                return false;
            }

            // ルームアンカーを取得する
            var rooms = new List<OVRAnchor>();
            await OVRAnchor.FetchAnchorsAsync(rooms, new OVRAnchor.FetchOptions
            {
                SingleComponentType = typeof(OVRRoomLayout)
            });

            if (rooms.Count == 0)
            {
                Debug.LogWarning("[GlobalMeshScanner] ルームアンカーが見つかりませんでした。");
                return false;
            }

            bool meshCreated = false;

            // 各ルームから子アンカーを取得し、GLOBAL_MESH を探す
            foreach (OVRAnchor room in rooms)
            {
                if (!room.TryGetComponent(out OVRAnchorContainer container))
                {
                    continue;
                }

                var children = new List<OVRAnchor>();
                await container.FetchAnchorsAsync(children);

                foreach (OVRAnchor child in children)
                {
                    // GLOBAL_MESH ラベルを持つアンカーのみ処理する
                    if (!IsGlobalMesh(child))
                    {
                        continue;
                    }

                    // アンカーの位置追跡を有効化する
                    if (!child.TryGetComponent(out OVRLocatable locatable))
                    {
                        continue;
                    }
                    await locatable.SetEnabledAsync(true);

                    // メッシュデータを取得して GameObject を生成する
                    if (child.TryGetComponent(out OVRTriangleMesh triangleMesh))
                    {
                        GameObject meshObject = CreateMeshObject(triangleMesh, locatable);
                        if (meshObject is not null)
                        {
                            _meshObjects.Add(meshObject);
                            meshCreated = true;
                        }
                    }
                }
            }

            if (!meshCreated)
            {
                Debug.LogWarning("[GlobalMeshScanner] Global Mesh が見つかりませんでした。");
            }

            return meshCreated;
        }

        /// <summary>
        /// アンカーが GLOBAL_MESH のセマンティックラベルを持つかどうかを判定する。
        /// </summary>
        private static bool IsGlobalMesh(OVRAnchor anchor)
        {
            if (!anchor.TryGetComponent(out OVRSemanticLabels labels))
            {
                return false;
            }

            var classifications = new List<OVRSemanticLabels.Classification>();
            labels.GetClassifications(classifications);

            // SceneMesh は GLOBAL_MESH に対応する列挙値
            return classifications.Contains(OVRSemanticLabels.Classification.SceneMesh);
        }

        /// <summary>
        /// OVRTriangleMesh のデータから Unity Mesh を持つ GameObject を生成する。
        /// MeshFilter（可視化）、MeshRenderer（描画）、MeshCollider（物理コリジョン）を設定する。
        /// </summary>
        private GameObject CreateMeshObject(OVRTriangleMesh triangleMesh, OVRLocatable locatable)
        {
            // メッシュの頂点数と三角形数を取得する
            if (!triangleMesh.TryGetCounts(out int vertexCount, out int triangleCount))
            {
                Debug.LogWarning("[GlobalMeshScanner] メッシュのカウント取得に失敗しました。");
                return null;
            }

            // NativeArray にメッシュデータを読み込む
            using var vertices = new NativeArray<Vector3>(vertexCount, Allocator.Temp);
            using var indices = new NativeArray<int>(triangleCount * 3, Allocator.Temp);

            // TryGetMesh は OpenXR 座標系から Unity 座標系への変換を自動で行う
            if (!triangleMesh.TryGetMesh(vertices, indices))
            {
                Debug.LogWarning("[GlobalMeshScanner] メッシュデータの取得に失敗しました。");
                return null;
            }

            // Unity Mesh を作成する
            var mesh = new Mesh
            {
                // 頂点数が多い場合に備えて 32bit インデックスを使用する
                indexFormat = IndexFormat.UInt32
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices.ToArray(), 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // GameObject を構築する
            var meshObject = new GameObject("GlobalMesh");
            meshObject.transform.SetParent(transform);
            meshObject.layer = _meshLayer;

            // アンカーの位置・回転をトラッキング空間から適用する
            if (locatable.TryGetSceneAnchorPose(out OVRLocatable.TrackingSpacePose trackingPose))
            {
                // TrackingSpacePose の Position/Rotation は Nullable なので値を取り出す
                Vector3 position = trackingPose.Position ?? Vector3.zero;
                Quaternion rotation = trackingPose.Rotation ?? Quaternion.identity;
                meshObject.transform.SetPositionAndRotation(position, rotation);
            }

            // MeshFilter: メッシュデータの保持
            var meshFilter = meshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            // MeshRenderer: 半透明マテリアルで可視化
            var meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _meshMaterial;

            // MeshCollider: 物理コリジョン
            var meshCollider = meshObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            return meshObject;
        }

        /// <summary>
        /// 生成済みのメッシュ GameObject をすべて破棄する。
        /// </summary>
        private void ClearMeshObjects()
        {
            foreach (GameObject meshObject in _meshObjects)
            {
                if (meshObject is not null)
                {
                    Destroy(meshObject);
                }
            }

            _meshObjects.Clear();
        }

        /// <summary>
        /// メッシュの表示・非表示を切り替える。
        /// </summary>
        public void SetMeshVisible(bool visible)
        {
            foreach (GameObject meshObject in _meshObjects)
            {
                if (meshObject is not null)
                {
                    meshObject.SetActive(visible);
                }
            }
        }

        /// <summary>
        /// スキャン状態を更新し、イベントを発火する。
        /// </summary>
        private void SetState(ScanState state)
        {
            CurrentState = state;
            _onScanStateChanged?.Invoke(state);

            // 状態に対応するステータス文字列を通知する
            string message = state switch
            {
                ScanState.Idle => "Scan Start",
                ScanState.Scanning => "Scanning ...",
                ScanState.Completed => "Scan Complete",
                ScanState.Failed => "Scan Failed",
                _ => ""
            };
            _onStatusChanged?.Invoke(message);
        }
    }
}
