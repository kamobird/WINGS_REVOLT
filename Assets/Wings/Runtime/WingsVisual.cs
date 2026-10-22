using System.Collections.Generic;
using UnityEngine;

namespace Wings
{
    /// <summary>
    /// 仮モデル・マテリアル・エフェクトを生成する見た目の処理。身体の判断と当たり判定から分離する。
    /// </summary>
    public static class WingsVisual
    {
        static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        /// <summary>
        /// 同じ色のマテリアルを再利用し、描画用インスタンスの作成を抑える。
        /// </summary>
        public static Material Material(Color color)
        {
            if (materials.TryGetValue(color, out var m) && m)
                return m;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var template = Resources.Load<Material>("WingsSurface");
            m = template ? new Material(template) : new Material(shader);
            m.color = color;
            if (m.HasProperty("_Smoothness"))
                m.SetFloat("_Smoothness", .28f);
            materials[color] = m;
            return m;
        }

        /// <summary>
        /// プリミティブの見た目を作る。solid指定がない装飾はColliderを外し、視界や経路の妨害を防ぐ。
        /// </summary>
        public static GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, Transform parent = null, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent)
                go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Material(color);
            var collider = go.GetComponent<Collider>();
            if (!solid)
            {
                collider.enabled = false;
                Object.Destroy(collider);
            }

            return go;
        }

        /// <summary>
        /// 指定Prefabがあればそれを使い、なければプリミティブ製の鳥モデルを作る。
        /// </summary>
        public static Transform BirdModel(CharacterDefinition d, Transform parent)
        {
            if (d.visualPrefab)
                return Object.Instantiate(d.visualPrefab, parent).transform;
            var root = new GameObject("Visual (replace via CharacterDefinition)").transform;
            root.SetParent(parent, false);
            Color dark = new Color(.065f, .1f, .17f);
            Shape("Body", PrimitiveType.Sphere, new Vector3(0, .88f, 0), new Vector3(.9f, 1.3f, .75f), dark, root);
            Shape("Chest",
                PrimitiveType.Sphere,
                new Vector3(0, .85f, .22f),
                new Vector3(.66f, .9f, .45f),
                d.color * .8f + Color.white * .2f,
                root);
            Shape("Head", PrimitiveType.Sphere, new Vector3(0, 1.48f, .06f), Vector3.one * .72f, dark, root);
            Shape("Visor", PrimitiveType.Cube, new Vector3(0, 1.53f, .36f), new Vector3(.62f, .15f, .15f), d.color, root);
            Shape("Beak", PrimitiveType.Cube, new Vector3(0, 1.38f, .48f), new Vector3(.21f, .14f, .34f), new Color(1, .65f, .15f), root);
            for (int side = -1; side <= 1; side += 2)
            {
                var wing = Shape("Wing", PrimitiveType.Capsule, new Vector3(side * .55f, .9f, 0), new Vector3(.2f, .6f, .4f), d.color, root);
                wing.transform.localRotation = Quaternion.Euler(12, 0, side * 22);
                Shape("Foot",
                    PrimitiveType.Cube,
                    new Vector3(side * .24f, .17f, .14f),
                    new Vector3(.3f, .17f, .5f),
                    new Color(1, .65f, .15f),
                    root);
            }

            if (d.bird == Bird.Owl)
            {
                Shape("Left ear", PrimitiveType.Cube, new Vector3(-.27f, 1.83f, 0), new Vector3(.22f, .35f, .25f), d.color, root);
                Shape("Right ear", PrimitiveType.Cube, new Vector3(.27f, 1.83f, 0), new Vector3(.22f, .35f, .25f), d.color, root);
            }

            return root;
        }

        /// <summary>
        /// 能力や着弾を知らせる円形の仮エフェクトを生成する。
        /// </summary>
        public static void Ring(Vector3 at, float radius, Color color, float seconds = .45f)
        {
            var go = new GameObject("Pulse");
            go.transform.position = at;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.sharedMaterial = Material(color);
            line.widthMultiplier = .09f;
            line.positionCount = 65;
            for (int i = 0; i < 65; i++)
            {
                float a = i * Mathf.PI / 32;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, .08f, Mathf.Sin(a) * radius));
            }

            go.AddComponent<FadeEffect>().life = seconds;
        }

        /// <summary>
        /// 2点間のビーム表示を作る。命中判定自体は攻撃側の処理で済ませる。
        /// </summary>
        public static void Beam(Vector3 a, Vector3 b, Color color, float width = .14f)
        {
            var go = new GameObject("Beam");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Material(color);
            line.widthMultiplier = width;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            go.AddComponent<FadeEffect>().life = .18f;
        }

        /// <summary>
        /// 短時間で消える破片状の仮エフェクトを生成する。
        /// </summary>
        public static void Burst(Vector3 at, Color color, int count = 8)
        {
            for (int i = 0; i < count; i++)
            {
                var go = Shape("Spark", PrimitiveType.Cube, at, Vector3.one * Random.Range(.09f, .24f), color);
                var e = go.AddComponent<FadeEffect>();
                e.life = Random.Range(.2f, .55f);
                e.velocity = Random.onUnitSphere * Random.Range(2, 5);
            }
        }
    }

    /// <summary>
    /// 仮エフェクトを寿命まで移動させて消す。表示専用でダメージや認識情報を持たない。
    /// </summary>
    public sealed class FadeEffect : MonoBehaviour
    {
        public float life = .5f;
        public Vector3 velocity;
        float age;

        /// <summary>
        /// 進行可能な画面で寿命と位置を更新し、時間切れの仮エフェクトを消す。
        /// </summary>
        void Update()
        {
            if (WingsGame.Instance && WingsGame.Instance.State != GameState.Playing && WingsGame.Instance.State != GameState.Title)
                return;
            age += Time.deltaTime;
            transform.position += velocity * Time.deltaTime;
            if (age >= life)
                Destroy(gameObject);
        }
    }
}
