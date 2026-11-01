using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 장면 전체를 코드로 구성한다. 빈 씬의 오브젝트 하나에 붙이면 동작한다.
public class CyclopsGame : MonoBehaviour
{
    public enum Eye { Closed, Warning, Open }
    public enum Goal { Wine, Stake, Harden, Feed, Stab, Sheep, Done, Caught }

    const float RunSpeed = 3f, WalkSpeed = 1.7f, SheepSpeed = 1.0f, ReachDist = 3f;
    const float StandHeight = 6.2f, RoamSpeed = 1.6f, RushSpeed = 4f, StumbleTime = 2.5f;
    static readonly Vector3 HeadPos = new Vector3(0, 1.4f, 2);
    static readonly Vector3 GatePos = new Vector3(0, 0, 15);
    static readonly Vector3 StakePos = new Vector3(8, 0.4f, -12);
    static readonly Vector3 WinePos = new Vector3(-11, .35f, -13);
    static readonly Vector3 FirePos = new Vector3(10, 0, -14);
    static readonly Vector3 FeatherPos = new Vector3(.5f, .6f, 8.5f);
    static readonly Vector3[] CrewPos = { new Vector3(-11.5f, 0, -4), new Vector3(11.5f, 0, -3.5f), new Vector3(11, 0, 10) };
    static readonly string[] CrewName = { "엘페노르", "에우릴로코스", "폴리테스" };

    // 눈을 찌른 뒤에 잡히면 그 자리부터 다시 시작한다
    static bool saved, savedNobody, savedCheese, savedFeather, savedFeatherGone, seenIntro;
    static int savedStones, savedSocks;
    static int[] savedCrew;

    public Goal goal = Goal.Wine;
    public Eye eye = Eye.Closed;
    public bool simHold;
    public bool hasSheep;

    CharacterController player;
    Camera cam;
    Transform giant, body, armL, armR, lid, pupil, stake, sheepHeld;
    Transform[] sheep;
    readonly Transform[] sheepHead = new Transform[3];
    readonly Transform[][] sheepLegs = new Transform[3][];
    readonly Vector3[] sheepGoal = new Vector3[3];
    readonly float[] sheepWait = new float[3];
    float bleatT = 4;
    AudioClip bleat;
    Light beam, eyeGlow, torch;
    AudioSource breath, heart, sfx;
    AudioClip splash, crack, stabClip, stomp;
    Texture2D vignette, black;
    GUIStyle style, small;
    Vector3[] puddles = { new Vector3(-4, 0, -9), new Vector3(3, 0, -6), new Vector3(-6, 0, -2), new Vector3(5, 0, -1), new Vector3(0, 0, -11),
        new Vector3(-3, 0, 6), new Vector3(5, 0, 10), new Vector3(0, 0, 12), new Vector3(-9, 0, 13) };
    Vector3[] bones = { new Vector3(1, 0, -13), new Vector3(-8, 0, -6), new Vector3(9, 0, -5), new Vector3(-2, 0, -3), new Vector3(3, 0, 7), new Vector3(-7, 0, 8), new Vector3(8, 0, 13) };
    bool[] boneUsed;
    float yaw, pitch, eyeTimer = 5, sweepBase, seen, heartT, bob, breathT, giantAlert, stumbleT, stompT, armPhase, curSpeed, smoothYaw, stepT, grip = 1, handTimer = 3, msgT;
    bool scared, wasInPuddle, moving, running, canStab, testing;
    int stickId = -1;
    Vector2 stick, stickOrigin;
    Vector3 curV;
    Texture2D dot;
    Transform model;
    float modelYaw, closeUp, camDist = 3;
    float noise;
    int handState;
    string msg = "";
    Transform nearSheep, hand;
    Vector3 lastMouse, noisePos, roamTo;

    // 이야기 카드: 떠 있는 동안 게임이 멈춘다
    string card;
    string[] choices;
    System.Action<int> cardDone;
    string ending = "";

    // 아이템과 동료
    Transform wine, boulder, feather, rock;
    readonly Transform[] crew = new Transform[3];
    readonly int[] crewState = new int[3]; // 0 묶임, 1 양에게 가는 중, 2 숨음
    int stones, socks;
    bool cheese, hasFeather, nobody = true;
    float hardenT, stabPower, safeT, rockT = -1, flash;
    Vector3 rockFrom, rockTo;
    string act;
    System.Action onAct;
    float Fury => nobody ? 1 : 1.35f;

    void Start()
    {
        Application.targetFrameRate = 60;
        bool auto = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0;
        testing = auto;
        boneUsed = new bool[bones.Length];
        BuildCave();
        BuildGiant();
        BuildPlayer();
        BuildAudio();
        vignette = MakeVignette();
        black = Texture2D.whiteTexture;
        dot = new Texture2D(48, 48, TextureFormat.RGBA32, false);
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
                dot.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(23.5f - new Vector2(x - 23.5f, y - 23.5f).magnitude)));
        dot.Apply();
        if (saved) Resume();
        if (auto)
        {
            Application.runInBackground = true;
            StartCoroutine(saved ? ResumeTest() : AutoTest());
        }
        else if (!seenIntro && !saved)
        {
            seenIntro = true;
            Pages(null,
                "트로이 전쟁이 끝나고, 오디세우스는\n고향 이타카로 돌아가는 길이에요.\n\n먹을 것을 찾아 들어간 동굴의 주인은\n외눈박이 거인 폴리페모스였어요.",
                "거인은 커다란 바위로 입구를 막고 잠들었어요.\n동료들은 동굴 곳곳에 묶여 있어요.",
                "거인을 죽이면 바위를 치울 수 없어\n모두 갇혀요.\n\n힘이 아니라 꾀로 빠져나가야 해요.");
        }
    }

    // 찌른 뒤의 상태로 장면을 되돌린다
    void Resume()
    {
        nobody = savedNobody; stones = savedStones; socks = savedSocks; cheese = savedCheese; hasFeather = savedFeather;
        for (int i = 0; i < 3; i++)
        {
            crewState[i] = savedCrew[i] > 0 ? 2 : 0;
            crew[i].gameObject.SetActive(crewState[i] == 0);
        }
        feather.gameObject.SetActive(!savedFeatherGone);
        Destroy(wine.gameObject);
        goal = Goal.Stab;
        Stab(true);
        stumbleT = 0;
        player.enabled = false; player.transform.position = new Vector3(-9, 1, -10); player.enabled = true;
        Say("눈을 찌른 뒤부터 다시 시작해요", 3);
    }

    GameObject Prim(PrimitiveType t, Vector3 pos, Vector3 scale, Color col, bool solid = true, Transform parent = null)
    {
        var o = GameObject.CreatePrimitive(t);
        if (parent) o.transform.SetParent(parent, false);
        if (parent) o.transform.localPosition = pos; else o.transform.position = pos;
        o.transform.localScale = scale;
        var r = o.GetComponent<Renderer>();
        r.material = new Material(Resources.Load<Material>("Base")) { color = col };
        if (!solid) Destroy(o.GetComponent<Collider>());
        return o;
    }

    void BuildCave()
    {
        var rock = new Color(.23f, .19f, .16f);
        Prim(PrimitiveType.Cube, new Vector3(0, -.5f, 0), new Vector3(26, 1, 40), new Color(.16f, .13f, .11f));
        Prim(PrimitiveType.Cube, new Vector3(0, 9, 0), new Vector3(26, 1, 40), rock);
        Prim(PrimitiveType.Cube, new Vector3(-13, 4, 0), new Vector3(1, 10, 40), rock);
        Prim(PrimitiveType.Cube, new Vector3(13, 4, 0), new Vector3(1, 10, 40), rock);
        Prim(PrimitiveType.Cube, new Vector3(0, 4, -20), new Vector3(26, 10, 1), rock);
        Prim(PrimitiveType.Cube, new Vector3(-8, 4, 20), new Vector3(10, 10, 1), rock);
        Prim(PrimitiveType.Cube, new Vector3(8, 4, 20), new Vector3(10, 10, 1), rock);
        // 양 없이는 나갈 수 없도록 입구를 막는 보이지 않는 벽
        Destroy(Prim(PrimitiveType.Cube, new Vector3(0, 4, 19), new Vector3(6, 10, 1), rock).GetComponent<Renderer>());
        // 눈빛을 피해 숨을 수 있는 바위
        foreach (var r in new[] { new Vector3(-6, 1.2f, -7), new Vector3(5, 1.2f, -8), new Vector3(1, 1.2f, -5), new Vector3(-9, 1.2f, 0), new Vector3(9, 1.2f, 1) })
            Prim(PrimitiveType.Sphere, r, new Vector3(3.2f, 3, 3.2f), new Color(.3f, .25f, .21f));
        foreach (var p in puddles)
            Prim(PrimitiveType.Cylinder, p + Vector3.up * .02f, new Vector3(2.6f, .01f, 2.6f), new Color(.1f, .28f, .36f), false);
        foreach (var b in bones)
            Prim(PrimitiveType.Capsule, b + Vector3.up * .08f, new Vector3(.12f, .4f, .12f), new Color(.85f, .82f, .7f), false).transform.rotation = Quaternion.Euler(90, b.x * 40, 0);

        Prim(PrimitiveType.Sphere, new Vector3(10, .3f, -14), new Vector3(1.2f, .6f, 1.2f), new Color(.9f, .35f, .08f), false);
        var fire = new GameObject("Fire").AddComponent<Light>();
        fire.type = LightType.Point; fire.color = new Color(1, .5f, .2f); fire.range = 16; fire.intensity = 2.2f;
        fire.transform.position = new Vector3(10, 1.5f, -14);

        // 난파선에서 건진 포도주 부대
        wine = new GameObject("Wine").transform;
        wine.position = WinePos;
        Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(.55f, .7f, .55f), new Color(.34f, .12f, .1f), false, wine);
        Prim(PrimitiveType.Cylinder, new Vector3(0, .42f, 0), new Vector3(.14f, .1f, .14f), new Color(.75f, .65f, .4f), false, wine);

        // 입구를 막은 바위. 눈을 찌르면 거인이 치운다
        boulder = Prim(PrimitiveType.Sphere, new Vector3(0, 3, 19.3f), new Vector3(8, 7.5f, 3), new Color(.3f, .25f, .21f), false).transform;

        // 거인 등 뒤에 숨은 아테나의 깃털
        feather = Prim(PrimitiveType.Capsule, FeatherPos, new Vector3(.12f, .32f, .05f), new Color(.75f, .9f, 1), false).transform;
        feather.rotation = Quaternion.Euler(0, 0, 25);
        var shine = new GameObject("Shine").AddComponent<Light>();
        shine.type = LightType.Point; shine.color = new Color(.6f, .8f, 1); shine.range = 4; shine.intensity = 1.5f;
        shine.transform.SetParent(feather, false);

        for (int i = 0; i < 3; i++) crew[i] = MakeCrew(CrewPos[i]);

        stake = Prim(PrimitiveType.Cylinder, StakePos, new Vector3(.14f, 1.1f, .14f), new Color(.42f, .25f, .1f), false).transform;
        stake.rotation = Quaternion.Euler(0, 0, 80);

        sheep = new Transform[3];
        var spots = new[] { new Vector3(-9, 0, 5), new Vector3(9, 0, 7), new Vector3(-5, 0, 10) };
        for (int i = 0; i < 3; i++) sheep[i] = MakeSheep(spots[i], i);

        // 천장의 종유석
        var drip = new System.Random(5);
        for (int i = 0; i < 26; i++)
        {
            float len = .5f + (float)drip.NextDouble() * 1.3f;
            Prim(PrimitiveType.Capsule, new Vector3((float)drip.NextDouble() * 24 - 12, 8.5f - len * .7f, (float)drip.NextDouble() * 38 - 19), new Vector3(.3f, len, .3f), rock * 1.25f, false);
        }

        var door = new GameObject("Daylight").AddComponent<Light>();
        door.type = LightType.Point; door.color = new Color(.65f, .78f, 1); door.range = 11; door.intensity = 1.6f;
        door.transform.position = new Vector3(0, 3, 18.5f);
    }

    // 묶여 앉아 있는 동료
    Transform MakeCrew(Vector3 at)
    {
        var c = new GameObject("Crew").transform;
        c.position = at;
        c.rotation = Quaternion.LookRotation(new Vector3(-at.x, 0, -at.z));
        Prim(PrimitiveType.Capsule, new Vector3(0, .5f, 0), new Vector3(.45f, .5f, .38f), new Color(.2f, .32f, .45f), false, c);
        Prim(PrimitiveType.Sphere, new Vector3(0, 1.12f, 0), Vector3.one * .32f, new Color(.82f, .62f, .46f), false, c);
        Prim(PrimitiveType.Sphere, new Vector3(0, 1.23f, -.03f), new Vector3(.35f, .2f, .35f), new Color(.2f, .14f, .1f), false, c);
        Prim(PrimitiveType.Cylinder, new Vector3(0, .6f, 0), new Vector3(.52f, .05f, .45f), new Color(.7f, .6f, .4f), false, c);
        return c;
    }

    // 풀려난 동료는 양에게 달려가 배 밑으로 숨는다
    void CrewLife()
    {
        for (int i = 0; i < 3; i++)
        {
            if (crewState[i] != 1) continue;
            Vector3 to = sheep[i].position; to.y = 0;
            Vector3 at = crew[i].position;
            crew[i].position = Vector3.MoveTowards(at, to, 2.4f * Time.deltaTime);
            if (to != at) crew[i].rotation = Quaternion.LookRotation(to - at);
            if (Vector3.Distance(crew[i].position, to) < .4f) { crewState[i] = 2; crew[i].gameObject.SetActive(false); }
        }
    }

    public void Free(int i)
    {
        crewState[i] = 1;
        crew[i].GetChild(3).gameObject.SetActive(false);
        sfx.PlayOneShot(crack, .4f);
        if (i == 0) { stones += 3; Say("엘페노르: 대장! 돌멩이 세 개 드릴게요\n소리로 거인을 속이세요", 4); }
        else if (i == 1) { socks = 3; Say("에우릴로코스: 양털 덧신이에요\n웅덩이와 뼈 소리를 세 번 막아 줘요", 4); }
        else { cheese = true; Say("폴리테스: 거인의 치즈를 훔쳤어요\n양에 매달릴 때 힘이 빠지면 드세요", 4); }
    }

    Transform MakeSheep(Vector3 at, int index)
    {
        var s = new GameObject("Sheep").transform;
        s.position = at;
        var rng = new System.Random(index + 3);
        var wool = new Color(.86f, .84f, .78f);
        // 몽실몽실한 털은 작은 공을 겹쳐 만든다
        Prim(PrimitiveType.Sphere, new Vector3(0, .75f, 0), new Vector3(.95f, .8f, 1.35f), wool, false, s);
        for (int i = 0; i < 14; i++)
        {
            float a = (float)rng.NextDouble() * 6.28f, z = (float)rng.NextDouble() * 1.1f - .55f, size = .34f + (float)rng.NextDouble() * .16f;
            Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * .42f, .75f + Mathf.Sin(a) * .34f, z), Vector3.one * size, wool * (.93f + (float)rng.NextDouble() * .09f), false, s);
        }
        var dark = new Color(.13f, .11f, .1f);
        var head = new GameObject("Head").transform;
        head.SetParent(s, false);
        head.localPosition = new Vector3(0, .95f, .78f);
        Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(.36f, .38f, .48f), dark, false, head);
        Prim(PrimitiveType.Sphere, new Vector3(0, .17f, -.03f), new Vector3(.36f, .2f, .3f), wool, false, head);
        foreach (int side in new[] { -1, 1 })
        {
            Prim(PrimitiveType.Sphere, new Vector3(side * .24f, .06f, -.04f), new Vector3(.26f, .09f, .13f), dark, false, head);
            Prim(PrimitiveType.Sphere, new Vector3(side * .13f, .07f, .15f), Vector3.one * .07f, new Color(.95f, .85f, .55f), false, head);
        }
        var legs = new Transform[4];
        int n = 0;
        foreach (var p in new[] { new Vector3(-.27f, .35f, .42f), new Vector3(.27f, .35f, .42f), new Vector3(-.27f, .35f, -.42f), new Vector3(.27f, .35f, -.42f) })
            legs[n++] = Prim(PrimitiveType.Cylinder, p, new Vector3(.11f, .35f, .11f), dark, false, s).transform;
        sheepHead[index] = head; sheepLegs[index] = legs;
        sheepGoal[index] = at; sheepWait[index] = index * 1.7f + 1;
        return s;
    }

    // 양은 풀을 뜯듯 고개를 숙이고, 이따금 몇 걸음 옮기며 운다
    void SheepLife()
    {
        bleatT -= Time.deltaTime;
        for (int i = 0; i < sheep.Length; i++)
        {
            var s = sheep[i];
            bool held = s == sheepHeld;
            Vector3 at = s.position; at.y = 0;
            float pace = held ? curSpeed : 0;
            if (!held)
            {
                sheepWait[i] -= Time.deltaTime;
                if (Vector3.Distance(at, sheepGoal[i]) > .1f && sheepWait[i] <= 0)
                {
                    pace = .45f;
                    Vector3 next = Vector3.MoveTowards(at, sheepGoal[i], pace * Time.deltaTime);
                    if (next != at) s.rotation = Quaternion.Slerp(s.rotation, Quaternion.LookRotation(next - at), Time.deltaTime * 3);
                    s.position = next;
                }
                else if (sheepWait[i] <= 0)
                {
                    sheepWait[i] = Random.Range(3f, 7f);
                    sheepGoal[i] = new Vector3(Mathf.Clamp(at.x + Random.Range(-3f, 3f), -11, 11), 0, Mathf.Clamp(at.z + Random.Range(-3f, 3f), 4, 13));
                }
            }
            float graze = !held && pace == 0 ? Mathf.Max(0, Mathf.Sin(Time.time * .5f + i * 2)) : 0;
            sheepHead[i].localRotation = Quaternion.Euler(graze * 45 + Mathf.Sin(Time.time * 5 + i) * 2 * graze, 0, 0);
            for (int l = 0; l < 4; l++)
                sheepLegs[i][l].localRotation = Quaternion.Euler(pace > .05f ? Mathf.Sin(Time.time * 10 + l * 3.14f * .5f) * 22 : 0, 0, 0);
        }
        if (bleatT <= 0)
        {
            bleatT = Random.Range(5f, 11f);
            var one = sheep[Random.Range(0, sheep.Length)];
            sfx.PlayOneShot(bleat, Mathf.Clamp01(1.1f - Vector3.Distance(player.transform.position, one.position) / 24) * .6f);
        }
    }

    void BuildGiant()
    {
        giant = new GameObject("Giant").transform;
        giant.position = HeadPos;
        var skin = new Color(.5f, .42f, .32f);
        // 쓰러져 잠든 몸은 머리 뒤쪽으로 뻗어 있다
        body = Prim(PrimitiveType.Capsule, new Vector3(0, -.1f, 5), new Vector3(4.2f, 5, 3), skin, false, giant).transform;
        body.localRotation = Quaternion.Euler(90, 0, 0);
        armL = Prim(PrimitiveType.Capsule, new Vector3(-2.5f, -2.8f, -.4f), new Vector3(.9f, 2.3f, .9f), skin, false, giant).transform;
        armR = Prim(PrimitiveType.Capsule, new Vector3(2.5f, -2.8f, -.4f), new Vector3(.9f, 2.3f, .9f), skin, false, giant).transform;
        hand = Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(2.2f, 1, 2.8f), skin, false).transform;
        for (int i = 0; i < 4; i++)
            Prim(PrimitiveType.Capsule, new Vector3(-.36f + i * .24f, -.1f, .62f), new Vector3(.17f, .32f, .2f), skin, false, hand).transform.localRotation = Quaternion.Euler(80, 0, 0);
        hand.gameObject.SetActive(false);
        armL.gameObject.SetActive(false); armR.gameObject.SetActive(false);
        Prim(PrimitiveType.Sphere, Vector3.zero, new Vector3(2.8f, 2.8f, 2.8f), skin, false, giant);
        Prim(PrimitiveType.Sphere, new Vector3(0, .3f, -1.1f), new Vector3(1.05f, 1.05f, .8f), new Color(.92f, .9f, .82f), false, giant);
        pupil = Prim(PrimitiveType.Sphere, new Vector3(0, .3f, -1.47f), new Vector3(.42f, .42f, .16f), new Color(.03f, .02f, .02f), false, giant).transform;
        Prim(PrimitiveType.Sphere, new Vector3(0, .3f, -1.44f), new Vector3(.72f, .72f, .2f), new Color(.75f, .32f, .05f), false, giant);
        lid = Prim(PrimitiveType.Sphere, new Vector3(0, .3f, -1.1f), new Vector3(1.15f, 1.15f, .98f), skin * .85f, false, giant).transform;

        // 눈은 빛을 내뿜으므로 눈 자체도 밝게 비춘다
        eyeGlow = new GameObject("EyeGlow").AddComponent<Light>();
        eyeGlow.type = LightType.Point; eyeGlow.range = 4; eyeGlow.intensity = 4;
        eyeGlow.color = new Color(1, .8f, .55f);
        eyeGlow.transform.SetParent(giant, false);
        eyeGlow.transform.localPosition = new Vector3(0, .3f, -2.5f);
        eyeGlow.enabled = false;

        // 거인의 얼굴: 눈썹, 귀, 코, 이빨
        Prim(PrimitiveType.Cube, new Vector3(0, 1.02f, -1.02f), new Vector3(1.7f, .22f, .34f), skin * .55f, false, giant).transform.localRotation = Quaternion.Euler(-18, 0, 0);
        foreach (int side in new[] { -1, 1 })
            Prim(PrimitiveType.Sphere, new Vector3(side * 1.42f, .15f, 0), new Vector3(.4f, .9f, .55f), skin * .9f, false, giant);
        Prim(PrimitiveType.Sphere, new Vector3(0, -.32f, -1.36f), new Vector3(.42f, .38f, .34f), skin * .92f, false, giant);
        Prim(PrimitiveType.Cube, new Vector3(0, -.8f, -1.16f), new Vector3(1.25f, .24f, .3f), new Color(.08f, .02f, .02f), false, giant);
        for (int i = 0; i < 4; i++)
            Prim(PrimitiveType.Cube, new Vector3(-.45f + i * .3f, -.74f, -1.32f), new Vector3(.17f, .22f, .08f), new Color(.8f, .74f, .55f), false, giant);

        beam = new GameObject("EyeBeam").AddComponent<Light>();
        beam.type = LightType.Spot; beam.spotAngle = 44; beam.range = 30; beam.intensity = 9;
        beam.color = new Color(1, .85f, .55f);
        beam.transform.SetParent(giant, false);
        beam.transform.localPosition = new Vector3(0, .3f, -1.5f);
        beam.enabled = false;
    }

    void BuildPlayer()
    {
        var go = new GameObject("Player");
        go.transform.position = new Vector3(-9, 1, -16);
        player = go.AddComponent<CharacterController>();
        player.height = 1.7f; player.radius = .35f;
        cam = new GameObject("Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black; cam.fieldOfView = 72; cam.nearClipPlane = .05f;
        cam.gameObject.AddComponent<AudioListener>();
        torch = cam.gameObject.AddComponent<Light>();
        torch.type = LightType.Point; torch.range = 10; torch.intensity = .6f; torch.color = new Color(1, .75f, .55f);
        yaw = smoothYaw = 35; pitch = 14;

        // 3인칭으로 보이는 주인공
        model = new GameObject("Hero").transform;
        model.SetParent(go.transform, false);
        var tunic = new Color(.62f, .23f, .14f);
        Prim(PrimitiveType.Capsule, Vector3.zero, new Vector3(.5f, .62f, .4f), tunic, false, model);
        Prim(PrimitiveType.Sphere, new Vector3(0, .72f, 0), new Vector3(.34f, .34f, .34f), new Color(.82f, .62f, .46f), false, model);
        Prim(PrimitiveType.Sphere, new Vector3(0, .84f, -.03f), new Vector3(.37f, .22f, .37f), new Color(.14f, .09f, .06f), false, model);
    }

    void BuildAudio()
    {
        int rate = 44100;
        var rng = new System.Random(7);
        float[] Make(float secs, System.Func<float, float, float> f)
        {
            var d = new float[(int)(secs * rate)];
            for (int i = 0; i < d.Length; i++) d[i] = f(i / (float)rate, (float)rng.NextDouble() * 2 - 1);
            return d;
        }
        AudioClip Clip(string n, float[] d) { var c = AudioClip.Create(n, d.Length, 1, rate, false); c.SetData(d, 0); return c; }

        float low = 0;
        var br = Clip("breath", Make(5, (t, n) => { low += (n - low) * .03f; float e = Mathf.Pow(Mathf.Sin(t * Mathf.PI / 5 * 2) * .5f + .5f, 2); return low * 3f * e; }));
        var hb = Clip("heart", Make(.5f, (t, n) => Mathf.Sin(t * 2 * Mathf.PI * 55) * (Mathf.Exp(-t * 18) + .7f * Mathf.Exp(-Mathf.Abs(t - .22f) * 30))));
        splash = Clip("splash", Make(.5f, (t, n) => n * Mathf.Exp(-t * 7) * .7f));
        stomp = Clip("stomp", Make(.4f, (t, n) => Mathf.Sin(t * 2 * Mathf.PI * 42) * Mathf.Exp(-t * 11)));
        bleat = Clip("bleat", Make(.9f, (t, n) => Mathf.Sin(t * 2 * Mathf.PI * (210 - t * 40)) * (.55f + .45f * Mathf.Sin(t * 2 * Mathf.PI * 19)) * Mathf.Sin(Mathf.Clamp01(t / .9f) * Mathf.PI) * .7f));
        crack = Clip("crack", Make(.15f, (t, n) => n * Mathf.Exp(-t * 40)));
        stabClip = Clip("stab", Make(1.2f, (t, n) => (n * .5f + Mathf.Sin(t * 900 * (1.3f - t))) * Mathf.Exp(-t * 2.5f) * .7f));

        breath = gameObject.AddComponent<AudioSource>();
        breath.clip = br; breath.loop = true; breath.Play();
        heart = gameObject.AddComponent<AudioSource>();
        heart.clip = hb;
        sfx = gameObject.AddComponent<AudioSource>();
    }

    Texture2D MakeVignette()
    {
        var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 44f;
                t.SetPixel(x, y, new Color(0, 0, 0, Mathf.SmoothStep(.25f, 1, d)));
            }
        t.Apply();
        return t;
    }

    // 어깨 뒤를 따라가다가 찌르기 직전에는 거인의 눈앞으로 바짝 다가간다
    void LateUpdate()
    {
        float d = Vector3.Distance(player.transform.position, HeadPos);
        float close = goal == Goal.Stab || goal == Goal.Feed ? Mathf.InverseLerp(5, 3, d) : 0;
        closeUp = Mathf.Lerp(closeUp, close, 1 - Mathf.Exp(-5 * Time.deltaTime));
        Quaternion rot = Quaternion.Euler(pitch, smoothYaw, 0);
        // 눈앞까지 다가가면 주인공이 눈을 가리지 않도록 숨긴다
        model.gameObject.SetActive(closeUp < .55f);
        Vector3 pivot = player.transform.position + Vector3.up * (hasSheep ? .1f : .75f);
        Vector3 back = rot * new Vector3(.45f * (1 - closeUp), .15f, -Mathf.Lerp(hasSheep ? 2.2f : 3.1f, .6f, closeUp));
        float dist = back.magnitude;
        if (Physics.SphereCast(pivot, .2f, back.normalized, out var hit, dist, ~0, QueryTriggerInteraction.Ignore))
            dist = Mathf.Max(.3f, hit.distance);
        camDist = Mathf.Min(dist, Mathf.Lerp(camDist, dist, 1 - Mathf.Exp(-8 * Time.deltaTime)));
        cam.transform.SetPositionAndRotation(pivot + back.normalized * camDist, rot);
    }

    void Update()
    {
        if (card != null) { CardInput(); return; }
        if (goal == Goal.Done || goal == Goal.Caught)
        {
            if (Input.GetMouseButtonDown(0) && !testing) SceneManager.LoadScene(0);
            return;
        }
        safeT -= Time.deltaTime;
        flash = Mathf.Max(0, flash - Time.deltaTime);
        Move();
        SheepLife();
        CrewLife();
        RockFlight();
        Objectives();
        EyeLogic();
        Feel();
    }

    Rect StabRect => new Rect(Screen.width * .2f, Screen.height * .72f, Screen.width * .6f, Screen.height * .1f);

    Rect ItemRect(int slot)
    {
        float s = Mathf.Min(Screen.width * .2f, Screen.height * .1f);
        return new Rect(Screen.width - s * 1.15f, Screen.height * .58f - slot * s * 1.15f, s, s);
    }

    Rect ChoiceRect(int i) => new Rect(Screen.width * .1f, Screen.height * (.62f + i * .12f), Screen.width * .8f, Screen.height * .1f);

    // 조이스틱을 쥔 채 다른 손가락으로 눌러도 버튼이 먹도록 터치를 직접 살핀다
    bool Pressed(Rect r)
    {
        if (Input.touchCount > 0)
        {
            foreach (var t in Input.touches)
                if (t.phase == TouchPhase.Began && r.Contains(new Vector2(t.position.x, Screen.height - t.position.y))) return true;
            return false;
        }
        Vector2 m = Input.mousePosition;
        if (!Input.GetMouseButtonDown(0) || !r.Contains(new Vector2(m.x, Screen.height - m.y))) return false;
        lastMouse = Input.mousePosition;
        return true;
    }

    void Pages(System.Action then, params string[] pages)
    {
        void Show(int i) => Card(pages[i], null, _ => { if (i + 1 < pages.Length) Show(i + 1); else then?.Invoke(); });
        Show(0);
    }

    void Card(string text, string[] options, System.Action<int> then)
    {
        // 자동 시험에서는 넘기기만 하는 카드를 건너뛴다
        if (testing && options == null) { then?.Invoke(0); return; }
        card = text; choices = options; cardDone = then;
        stick = Vector2.zero; stickId = -1; curV = Vector3.zero; curSpeed = 0; moving = false;
    }

    public void Choose(int i)
    {
        var then = cardDone;
        card = null; choices = null; cardDone = null;
        then?.Invoke(i);
    }

    void CardInput()
    {
        if (choices == null)
        {
            if (Input.GetMouseButtonDown(0) || Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) Choose(0);
            return;
        }
        for (int i = 0; i < choices.Length; i++)
            if (Pressed(ChoiceRect(i))) { Choose(i); return; }
    }

    public void ThrowRock()
    {
        if (stones <= 0 || rockT >= 0) return;
        stones--;
        Vector3 from = player.transform.position + Vector3.up * .8f, dir = Quaternion.Euler(0, smoothYaw, 0) * Vector3.forward;
        rockTo = Physics.Raycast(from, dir, out var hit, 11, ~0, QueryTriggerInteraction.Ignore) ? hit.point - dir * .3f : from + dir * 11;
        rockTo.y = .1f; rockFrom = from; rockT = 0;
        if (!rock) rock = Prim(PrimitiveType.Sphere, from, Vector3.one * .18f, new Color(.5f, .47f, .42f), false).transform;
        rock.gameObject.SetActive(true);
    }

    // 돌이 떨어진 곳에서 소리가 난다
    void RockFlight()
    {
        if (rockT < 0) return;
        rockT += Time.deltaTime / .7f;
        rock.position = Vector3.Lerp(rockFrom, rockTo, rockT) + Vector3.up * Mathf.Sin(Mathf.Clamp01(rockT) * Mathf.PI) * 1.8f;
        if (rockT < 1) return;
        rockT = -1;
        rock.gameObject.SetActive(false);
        sfx.PlayOneShot(crack);
        if (hasSheep)
        {
            handState = 0; handTimer = 5; hand.gameObject.SetActive(false);
            Say("거인이 돌 소리 쪽으로 손을 뻗어요", 2.5f);
        }
        else Alert(rockTo);
    }

    public void EatCheese()
    {
        cheese = false; grip = 1;
        Say("치즈를 먹었어요! 팔에 힘이 돌아와요", 2.5f);
    }

    void Move()
    {
        if (act != null && Pressed(StabRect)) { onAct(); return; }
        if (stones > 0 && Pressed(ItemRect(0))) { ThrowRock(); return; }
        if (cheese && hasSheep && Pressed(ItemRect(1))) { EatCheese(); return; }

        // 왼쪽 엄지는 그 자리에 생기는 조이스틱으로 이동하고, 오른쪽 엄지는 끌어서 둘러본다
        float stickRange = Screen.height * .08f;
        Vector2 look = Vector2.zero;
        if (Input.touchCount > 0)
        {
            foreach (var t in Input.touches)
            {
                if (t.phase == TouchPhase.Began && t.position.x < Screen.width * .5f && stickId < 0) { stickId = t.fingerId; stickOrigin = t.position; }
                if (t.fingerId == stickId)
                {
                    bool over = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                    stick = over ? Vector2.zero : Vector2.ClampMagnitude((t.position - stickOrigin) / stickRange, 1);
                    if (over) stickId = -1;
                }
                else if (t.phase == TouchPhase.Moved) look += t.deltaPosition;
            }
        }
        else
        {
            if (Input.GetMouseButtonDown(0)) { lastMouse = Input.mousePosition; stickOrigin = lastMouse; stickId = lastMouse.x < Screen.width * .5f ? 0 : -1; }
            if (Input.GetMouseButton(0))
            {
                if (stickId >= 0) stick = Vector2.ClampMagnitude(((Vector2)Input.mousePosition - stickOrigin) / stickRange, 1);
                else look = Input.mousePosition - lastMouse;
                lastMouse = Input.mousePosition;
            }
            else { stick = Vector2.zero; stickId = -1; }
        }
        Vector2 keys = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
        Vector2 input = simHold ? new Vector2(0, .6f) : keys != Vector2.zero ? keys * (Input.GetKey(KeyCode.LeftShift) ? 1 : .6f) : stick;
        bool walk = input.magnitude > .12f;
        running = !hasSheep && input.magnitude > .8f;
        float scale = 900f / Mathf.Max(Screen.height, 1);
        yaw += look.x * .22f * scale;
        pitch = Mathf.Clamp(pitch - look.y * .16f * scale, -50, 50);
        // 가볍게 출발하고 손을 떼면 바로 멈춘다. 크게 밀면 달리지만 소리가 난다
        float wanted = !walk ? 0 : hasSheep ? SheepSpeed : running ? RunSpeed : WalkSpeed;
        Vector3 wish = (player.transform.forward * input.y + player.transform.right * input.x).normalized * wanted;
        curV = Vector3.MoveTowards(curV, wish, Time.deltaTime * (walk ? 9 : 18));
        curSpeed = curV.magnitude;
        moving = curSpeed > .25f;
        smoothYaw = Mathf.LerpAngle(smoothYaw, yaw, 1 - Mathf.Exp(-18 * Time.deltaTime));
                player.transform.rotation = Quaternion.Euler(0, smoothYaw, 0);
        Vector3 v = curV;
        player.Move((v + Vector3.down * 3) * Time.deltaTime);
        bob += Time.deltaTime * curSpeed * 4.2f;
        stepT += Time.deltaTime * curSpeed;
        if (stepT > .85f) { stepT = 0; sfx.PlayOneShot(crack, .07f); }
        // 주인공은 움직이는 쪽을 보고, 양에 매달리면 배 밑에 눕는다
        if (curSpeed > .2f) modelYaw = Mathf.MoveTowardsAngle(modelYaw, Quaternion.LookRotation(curV).eulerAngles.y, 540 * Time.deltaTime);
        model.rotation = Quaternion.Euler(hasSheep ? -90 : 0, hasSheep ? smoothYaw : modelYaw, 0);
        model.localPosition = Vector3.up * (hasSheep ? -.52f : Mathf.Abs(Mathf.Sin(bob)) * .05f);
        if (sheepHeld)
        {
            sheepHeld.position = player.transform.position + Vector3.down * .72f + player.transform.forward * .1f;
            sheepHeld.rotation = player.transform.rotation;
        }

        // 소리: 물웅덩이와 뼈다귀. 양 밑에 숨어 있으면 들키지 않는다
        if (hasSheep) return;
        var flat = player.transform.position; flat.y = 0;
        // 달리면 소음이 쌓이고, 잠든 거인 곁에서는 걷는 소리도 들린다
        float toGiant = Vector3.Distance(flat, new Vector3(giant.position.x, 0, giant.position.z));
        noise += Time.deltaTime * ((running && moving ? .55f : 0) + (moving && toGiant < 7 ? .16f : 0) - .14f);
        // 덧신을 신으면 처음 몇 번의 소리는 나지 않는다
        bool Muffled() { if (socks <= 0) return false; socks--; Say($"덧신이 소리를 막았어요 (남은 {socks}번)", 2); return true; }
        noise = Mathf.Clamp01(noise);
        if (noise >= 1) { noise = .35f; Alert(flat); }
        bool inPuddle = false;
        foreach (var p in puddles) inPuddle |= Vector3.Distance(flat, p) < 1.3f;
        if (inPuddle && !wasInPuddle && moving && !Muffled()) { sfx.PlayOneShot(splash); Alert(flat); }
        wasInPuddle = inPuddle;
        for (int i = 0; i < bones.Length; i++)
            if (!boneUsed[i] && Vector3.Distance(flat, bones[i]) < .7f) { boneUsed[i] = true; if (!Muffled()) { sfx.PlayOneShot(crack); Alert(flat); } }
    }

    void Alert(Vector3 at)
    {
        if (goal == Goal.Sheep) { giantAlert = 6; noisePos = at; return; }
        Vector3 dir = at - HeadPos;
        sweepBase = Mathf.Atan2(dir.x, -dir.z) * Mathf.Rad2Deg;
        eye = Eye.Open; eyeTimer = 3.5f;
    }

    void Offer(string label, System.Action what) { if (act == null) { act = label; onAct = what; } }

    void Objectives()
    {
        var pos = player.transform.position;
        Vector3 me = new Vector3(pos.x, 0, pos.z);
        act = null; onAct = null;
        if (goal == Goal.Wine && Vector3.Distance(pos, WinePos) < 1.8f)
        {
            goal = Goal.Stake; stones += 2;
            wine.SetParent(model, false);
            wine.localPosition = new Vector3(-.3f, .05f, -.1f);
            wine.localScale = Vector3.one * .5f;
            Say("포도주와 돌멩이 2개를 챙겼어요\n돌을 던지면 소리로 거인을 속일 수 있어요", 4);
        }
        if (goal == Goal.Stake && Vector3.Distance(pos, StakePos) < 1.6f)
        {
            goal = Goal.Harden;
            stake.SetParent(model, false);
            stake.localPosition = new Vector3(.33f, .1f, .25f);
            stake.localRotation = Quaternion.Euler(75, 0, 0);
            stake.localScale = new Vector3(.07f, .55f, .07f);
        }
        // 말뚝 끝은 불 옆에 가만히 서서 달군다
        if (goal == Goal.Harden && Vector3.Distance(me, FirePos) < 3.4f && !moving)
        {
            hardenT += Time.deltaTime / 2.2f;
            stake.GetComponent<Renderer>().material.color = Color.Lerp(new Color(.42f, .25f, .1f), new Color(1, .35f, .05f), hardenT);
            if (hardenT >= 1) { goal = Goal.Feed; sfx.PlayOneShot(crack, .6f); Say("말뚝이 시뻘겋게 달아올랐어요", 3); }
        }
        // 얼굴 앞에 서면 눈이 번쩍 뜨이고 거인이 이름을 묻는다
        if (goal == Goal.Feed && Vector3.Distance(pos, HeadPos) < ReachDist) AskName();
        if (goal == Goal.Stab)
        {
            stabPower = Mathf.Max(0, stabPower - Time.deltaTime * .13f);
            canStab = eye == Eye.Closed && Vector3.Distance(pos, HeadPos) < ReachDist;
            if (canStab) Offer("말뚝을 밀어 넣어요! 연타", () =>
            {
                stabPower += .2f; sfx.PlayOneShot(crack, .3f);
                if (stabPower >= 1) Stab();
            });
        }
        if (goal < Goal.Done && !hasSheep)
            for (int i = 0; i < 3; i++)
            {
                int who = i;
                if (crewState[i] == 0 && Vector3.Distance(me, crew[i].position) < 1.9f) Offer($"{CrewName[i]} 풀어주기", () => Free(who));
            }
        if (feather.gameObject.activeSelf && Vector3.Distance(me, new Vector3(FeatherPos.x, 0, FeatherPos.z)) < 1.3f)
        {
            hasFeather = true; feather.gameObject.SetActive(false); sfx.PlayOneShot(bleat, .3f);
            Say("아테나의 깃털을 찾았어요!\n잡힐 뻔할 때 딱 한 번 지켜 줘요", 4);
        }
        if (goal == Goal.Sheep)
        {
            nearSheep = null;
            if (!hasSheep)
                foreach (var one in sheep)
                    if (Vector3.Distance(me, one.position) < 1.8f) nearSheep = one;
            if (nearSheep) Offer("양 배에 매달리기", GripSheep);
            GiantRoam(me);
            if (hasSheep && pos.z > 17 && Mathf.Abs(pos.x) < 3.2f) Escape();
        }
    }

    public void AskName()
    {
        scared = true; eye = Eye.Open; eyeTimer = 99; seen = 0;
        lid.gameObject.SetActive(false); beam.enabled = eyeGlow.enabled = true;
        sfx.PlayOneShot(stomp);
        goal = Goal.Stab;
        Card("눈이 번쩍 뜨였어요!\n\n\"작은 것아. 네 이름이 뭐냐?\"", new[] { "\"내 이름은 아무도 아니다\"", "\"나는 이타카의 오디세우스다!\"" }, c =>
        {
            nobody = c == 0;
            Destroy(wine.gameObject);
            string name = nobody ? "아무도" : "오디세우스";
            Pages(() =>
            {
                eye = Eye.Closed; eyeTimer = 13; beam.enabled = eyeGlow.enabled = false;
                Say("코 고는 동안 찌르세요!", 4);
            }, $"포도주 부대를 내밀자 거인이 벌컥벌컥 마셔요.\n\n\"달구나! {name}, 너는 맨 마지막에 먹어 주마.\"\n\n거인이 쓰러져 코를 골아요.");
        });
    }

    void Escape()
    {
        goal = Goal.Done; saved = false;
        const string quiet = "노를 저어 조용히 멀어졌어요.\n거인은 누구를 원망해야 할지 몰라 울부짖어요.";
        const string loud = "거인이 산꼭대기를 뜯어 던졌어요!\n바위가 뱃머리를 스치고, 거인은 아버지 포세이돈에게 빌어요.\n\"오디세우스가 고향에 닿지 못하게 하소서.\"";
        void Finish(bool shouted)
        {
            int saves = 0;
            foreach (int c in crewState) saves += c > 0 ? 1 : 0;
            string title = shouted ? "입이 가벼운 영웅" : saves == 3 ? "꾀 많은 오디세우스" : "조용한 생존자";
            string crewLine = saves == 3 ? "동료를 모두 데려왔어요" : saves == 0 ? "배에는 당신뿐이에요" : $"동료 {saves}명을 데려왔어요";
            ending = $"{(shouted ? loud : quiet)}\n\n칭호: {title}\n{crewLine} ({saves}/3)";
        }
        if (nobody)
            Card("양들과 함께 해변에 닿아 배에 올랐어요.\n절벽 위에서 거인이 울부짖어요.\n\n한마디 해 줄까요?", new[] { "\"내가 바로 오디세우스다!\"", "조용히 노를 젓는다" }, c => Finish(c == 0));
        else
            Pages(() => Finish(true), "양들과 함께 해변에 닿아 배에 올랐어요.\n거인은 당신의 이름을 알고 있어요.");
    }

    // 눈이 먼 거인은 더듬으며 돌아다니다가 소리가 나면 그곳으로 달려간다
    void GiantRoam(Vector3 me)
    {
        Vector3 g = giant.position;
        if (stumbleT > 0)
        {
            stumbleT -= Time.deltaTime;
            g.y = Mathf.Lerp(StandHeight, HeadPos.y, stumbleT / StumbleTime);
            g.x += Mathf.Sin(Time.time * 14) * .03f;
            giant.position = g;
            SwingArms(14);
            return;
        }
        if (hasSheep) { GateWatch(me); return; }
        Vector3 flat = new Vector3(g.x, 0, g.z);
        bool hears = giantAlert > 0;
        giantAlert -= Time.deltaTime;
        if (hears && Vector3.Distance(flat, noisePos) < .5f) giantAlert = 0;
        if (!hears && Vector3.Distance(flat, roamTo) < 1) roamTo = new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-16f, 15f));
        Vector3 target = hears ? noisePos : roamTo;
        float speed = (hears ? RushSpeed : RoamSpeed) * Fury;
        Vector3 next = Vector3.MoveTowards(flat, target, speed * Time.deltaTime);
        if (target != flat)
            giant.rotation = Quaternion.Slerp(giant.rotation, Quaternion.LookRotation(flat - target), Time.deltaTime * 3);
        giant.position = new Vector3(next.x, StandHeight, next.z);
        SwingArms(hears ? 9 : 3.5f);
        stompT -= Time.deltaTime;
        if (stompT <= 0)
        {
            stompT = hears ? .32f : .8f;
            sfx.PlayOneShot(stomp, Mathf.Clamp01(1.2f - Vector3.Distance(me, next) / 22));
        }
        if (!hasSheep && Vector3.Distance(next, me) < 2.5f) Catch();
    }

    // 양 배에 매달리면 들킨 줄 모르는 거인이 입구를 막고 앉아 지나가는 양을 손으로 더듬는다
    public void GripSheep()
    {
        hasSheep = true; sheepHeld = nearSheep; nearSheep = null; grip = 1; handTimer = 3;
        Say("거인이 입구를 막고 앉았어요\n양만 내보내며 손으로 더듬어요", 5);
    }

    void GateWatch(Vector3 me)
    {
        Vector3 next = Vector3.MoveTowards(new Vector3(giant.position.x, 0, giant.position.z), GatePos, RushSpeed * Time.deltaTime);
        giant.position = new Vector3(next.x, StandHeight, next.z);
        giant.rotation = Quaternion.Slerp(giant.rotation, Quaternion.identity, Time.deltaTime * 2);
        SwingArms(2);

        // 움직이면 팔 힘이 빠지고 멈추면 돌아온다. 그래서 무작정 기다릴 수도 없다
        grip = Mathf.Clamp01(grip + Time.deltaTime * (moving ? -.06f : .05f));
        if (grip <= 0)
        {
            hasSheep = false; sheepHeld = null; handState = 0; hand.gameObject.SetActive(false);
            sfx.PlayOneShot(splash); Say("손이 미끄러졌어요!", 3); Alert(me);
            return;
        }
        if (me.z < 6) return;
        handTimer -= Time.deltaTime;
        if (handState == 0 && handTimer <= 0) { handState = 1; handTimer = 1.3f; Say("손이 내려와요! 멈추세요", 1.3f); sfx.PlayOneShot(stomp); }
        else if (handState == 1 && handTimer <= 0) { handState = 2; handTimer = 2.5f; hand.gameObject.SetActive(true); }
        else if (handState == 2 && handTimer <= 0) { handState = 0; handTimer = Random.Range(3f, 5f) / Fury; hand.gameObject.SetActive(false); }
        if (handState == 2)
        {
            hand.position = me + Vector3.up * (1.6f + Mathf.Max(0, handTimer - 1.5f) * 3 + Mathf.Sin(Time.time * 6) * .12f);
            if (moving) Catch();
        }
    }

    void Say(string text, float secs) { msg = text; msgT = secs; }

    void SwingArms(float rate)
    {
        armPhase += Time.deltaTime * rate;
        float a = Mathf.Sin(armPhase) * 40;
        armL.localRotation = Quaternion.Euler(a, 0, -14);
        armR.localRotation = Quaternion.Euler(-a, 0, 14);
    }

    public void Stab(bool restoring = false)
    {
        goal = Goal.Sheep; canStab = false; eye = Eye.Closed;
        lid.gameObject.SetActive(false);
        pupil.GetComponent<Renderer>().material.color = new Color(.6f, 0, 0);
        stake.SetParent(giant, false);
        stake.localPosition = new Vector3(0, .3f, -1.8f);
        stake.localRotation = Quaternion.Euler(90, 0, 0);
        stake.localScale = new Vector3(.14f, 1.1f, .14f);
        // 누워 있던 몸을 머리 아래로 세운다
        body.localPosition = new Vector3(0, -3.6f, 0);
        body.localRotation = Quaternion.identity;
        body.localScale = new Vector3(4.2f, 2.6f, 3);
        armL.gameObject.SetActive(true); armR.gameObject.SetActive(true);
        stumbleT = StumbleTime;
        roamTo = new Vector3(0, 0, 10);
        beam.enabled = eyeGlow.enabled = false;
        Destroy(boulder.gameObject);
        if (restoring) return;

        sfx.PlayOneShot(stabClip);
        saved = true; savedNobody = nobody; savedStones = stones; savedSocks = socks; savedCheese = cheese;
        savedFeather = hasFeather; savedFeatherGone = !feather.gameObject.activeSelf;
        savedCrew = (int[])crewState.Clone();
        const string after = "거인이 입구의 바위를 치우고 앉았어요.\n이제 지나가는 것을 손으로 더듬어요.\n\n양 배에 매달려야 빠져나갈 수 있어요.";
        if (nobody)
            Pages(null, "비명에 이웃 키클롭스들이 몰려왔어요.\n\n\"폴리페모스! 누가 널 해쳤나?\"\n\"아무도! 아무도가 날 찔렀다!\"", "\"아무도 아니라면 신의 벌이겠지.\"\n\n이웃들은 웃으며 돌아갔어요.", after);
        else
            Pages(null, "\"오디세우스가 날 찔렀다!\"\n\n이름을 알게 된 거인이 더 사납게 날뛰어요.\n더 빠르고, 손도 더 자주 내려와요.", after);
    }

    void EyeLogic()
    {
        if (goal == Goal.Sheep) { breath.volume = 1; return; }
        eyeTimer -= Time.deltaTime;
        if (eye == Eye.Closed && eyeTimer <= 0)
        {
            eye = Eye.Warning; eyeTimer = 1.6f;
            Vector3 d = player.transform.position - HeadPos;
            sweepBase = Mathf.Atan2(d.x, -d.z) * Mathf.Rad2Deg + Random.Range(-35f, 35f);
        }
        else if (eye == Eye.Warning && eyeTimer <= 0) { eye = Eye.Open; eyeTimer = 5; }
        else if (eye == Eye.Open && eyeTimer <= 0) { eye = Eye.Closed; eyeTimer = Random.Range(4f, 7f); seen = 0; }

        lid.gameObject.SetActive(eye != Eye.Open);
        beam.enabled = eyeGlow.enabled = eye == Eye.Open;
        // 숨소리가 끊기면 곧 눈이 뜨인다는 신호
        breath.volume = Mathf.MoveTowards(breath.volume, eye == Eye.Closed ? 1 : 0, Time.deltaTime * 3);
        if (eye != Eye.Open) return;

        // 눈빛은 플레이어 쪽으로 서서히 따라온다
        Vector3 toMe = player.transform.position - HeadPos;
        sweepBase = Mathf.MoveTowardsAngle(sweepBase, Mathf.Atan2(toMe.x, -toMe.z) * Mathf.Rad2Deg, 13 * Time.deltaTime);
        float look = sweepBase + Mathf.Sin(Time.time * .9f) * 16;
        Quaternion q = Quaternion.Euler(4, 180 - look, 0);
        beam.transform.rotation = q;
        pupil.localPosition = new Vector3(Mathf.Clamp(look / 90f, -1, 1) * -.3f, .3f, -1.47f);

        Vector3 from = beam.transform.position, to = player.transform.position + Vector3.up * .5f - from;
        bool inCone = Vector3.Angle(q * Vector3.forward, to) < beam.spotAngle * .5f;
        bool clear = !Physics.Raycast(from, to.normalized, out var hit, to.magnitude) || hit.collider == player;
        // 빛 속에서 움직이면 금방 들킨다. 멈춰도 가까우면 천천히 들킨다
        bool lit = inCone && clear;
        float rate = !lit ? -1 : moving ? 2.2f : to.magnitude < 9 ? .7f : 0;
        seen = Mathf.Clamp01(seen + rate * Time.deltaTime);
        if (seen >= 1) Catch();
    }

    void Catch()
    {
        if (testing || safeT > 0) return;
        if (hasFeather)
        {
            // 아테나의 깃털이 한 번은 거인의 손을 막는다
            hasFeather = false; safeT = 4; flash = 1; seen = 0; noise = 0; giantAlert = 0;
            handState = 0; handTimer = 5; hand.gameObject.SetActive(false);
            if (goal != Goal.Sheep) { eye = Eye.Closed; eyeTimer = 6; beam.enabled = eyeGlow.enabled = false; }
            var p = player.transform.position;
            roamTo = new Vector3(Mathf.Clamp(-p.x, -10, 10), 0, Mathf.Clamp(-p.z, -16, 15));
            Say("아테나의 깃털이 타올라요!\n거인이 잠시 당신을 놓쳤어요", 3);
            return;
        }
        goal = Goal.Caught; breath.Stop();
    }

    void Feel()
    {
        float d = Vector3.Distance(player.transform.position, giant.position);
        float near = Mathf.InverseLerp(20, 3, d);
        breathT = Mathf.Lerp(breathT, near, Time.deltaTime * 2);
        heartT -= Time.deltaTime;
        if (heartT <= 0) { heart.volume = .35f + near * .65f; heart.Play(); heartT = Mathf.Lerp(1.1f, .42f, near); }
    }

    void Button(Rect r, string label)
    {
        GUI.color = new Color(.05f, .03f, .02f, .72f);
        GUI.DrawTexture(r, black);
        GUI.color = new Color(.93f, .85f, .6f);
        GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), black);
        GUI.color = Color.white;
        GUI.Label(r, label, style);
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
            style.font = Resources.Load<Font>("NotoSansKR");
            style.normal.textColor = new Color(.96f, .9f, .77f);
        }
        float h = Screen.height, w = Screen.width;
        style.fontSize = Mathf.RoundToInt(Mathf.Min(h * .026f, w * .046f));
        small = small ?? new GUIStyle(style);
        small.fontSize = Mathf.RoundToInt(style.fontSize * .72f);
        float a = Mathf.Clamp01(.35f + breathT * .65f);
        GUI.color = new Color(1, 1, 1, a);
        GUI.DrawTexture(new Rect(0, 0, w, h), vignette, ScaleMode.StretchToFill);
        GUI.color = Color.white;

        string line = goal switch
        {
            Goal.Wine => "난파선에서 건진 포도주 부대를 챙기세요",
            Goal.Stake => "모닥불 옆의 말뚝을 찾으세요",
            Goal.Harden => "불 옆에 가만히 서서 말뚝을 달구세요",
            Goal.Feed => eye == Eye.Open ? "움직이지 마세요" : "포도주를 들고 거인의 얼굴 앞으로",
            Goal.Stab => eye == Eye.Open ? "움직이지 마세요" : "잠든 거인의 눈앞으로",
            Goal.Sheep => hasSheep ? "천천히 동굴 입구로" : player.transform.position.z > 15 ? "양 없이는 나갈 수 없어요" : "소리 내지 말고 양 밑에 숨으세요",
            _ => ""
        };
        if (goal <= Goal.Sheep) GUI.Label(new Rect(0, h * .05f, w, h * .06f), line, style);
        if (goal == Goal.Wine)
        {
            GUI.Label(new Rect(0, h * .8f, w * .5f, h * .13f), "왼쪽 밀기: 이동\n크게 밀면 달리기", style);
            GUI.Label(new Rect(w * .5f, h * .8f, w * .5f, h * .13f), "오른쪽 끌기\n둘러보기", style);
        }
        // 눈빛에 들키는 정도는 화면 붉기, 소음은 위쪽 막대로 보여 준다
        if (seen > 0 && goal <= Goal.Sheep)
        {
            GUI.color = new Color(.7f, .05f, .02f, seen * .55f);
            GUI.DrawTexture(new Rect(0, 0, w, h), black);
        }
        if (noise > 0 && goal <= Goal.Sheep && !hasSheep)
        {
            GUI.color = noise > .7f ? new Color(.9f, .2f, .15f) : new Color(.93f, .85f, .6f);
            GUI.DrawTexture(new Rect(w * .2f, h * .035f, w * .6f * noise, h * .008f), black);
        }
        GUI.color = Color.white;
        if (stickId >= 0)
        {
            float big = h * .16f, small = h * .07f, cx = stickOrigin.x, cy = h - stickOrigin.y;
            GUI.color = new Color(1, 1, 1, .16f);
            GUI.DrawTexture(new Rect(cx - big / 2, cy - big / 2, big, big), dot);
            GUI.color = new Color(1, 1, 1, .4f);
            GUI.DrawTexture(new Rect(cx + stick.x * big / 2 - small / 2, cy - stick.y * big / 2 - small / 2, small, small), dot);
            GUI.color = Color.white;
        }
        msgT -= Time.deltaTime;
        if (msgT > 0 && goal <= Goal.Sheep) GUI.Label(new Rect(0, h * .13f, w, h * .12f), msg, style);
        if (hasSheep && goal == Goal.Sheep)
        {
            GUI.color = grip < .3f ? new Color(.9f, .2f, .15f) : new Color(.93f, .85f, .6f);
            GUI.DrawTexture(new Rect(w * .2f, h * .92f, w * .6f * grip, h * .012f), black);
            GUI.color = Color.white;
        }
        if (goal < Goal.Done)
        {
            if (act != null) Button(StabRect, act);
            if (goal == Goal.Stab && stabPower > 0)
            {
                GUI.color = new Color(.95f, .4f, .1f);
                GUI.DrawTexture(new Rect(w * .2f, h * .83f, w * .6f * stabPower, h * .014f), black);
                GUI.color = Color.white;
            }
            if (goal == Goal.Harden && hardenT > 0)
            {
                GUI.color = new Color(.95f, .4f, .1f);
                GUI.DrawTexture(new Rect(w * .2f, h * .83f, w * .6f * hardenT, h * .014f), black);
                GUI.color = Color.white;
            }
            if (stones > 0) Button(ItemRect(0), $"돌\n{stones}");
            if (cheese && hasSheep) Button(ItemRect(1), "치즈");
            int freed = 0;
            foreach (int c in crewState) freed += c > 0 ? 1 : 0;
            string bag = $"동료 {freed}/3" + (socks > 0 ? $"   덧신 {socks}" : "") + (cheese ? "   치즈" : "") + (hasFeather ? "   깃털" : "");
            GUI.Label(new Rect(0, h * .1f, w, h * .035f), bag, small);
        }
        if (flash > 0)
        {
            GUI.color = new Color(.7f, .9f, 1, flash * .6f);
            GUI.DrawTexture(new Rect(0, 0, w, h), black);
            GUI.color = Color.white;
        }
        if (goal >= Goal.Done && card == null)
        {
            GUI.color = new Color(0, 0, 0, .88f);
            GUI.DrawTexture(new Rect(0, 0, w, h), black);
            GUI.color = Color.white;
            string again = goal == Goal.Caught && saved ? "터치해서 찌른 뒤부터 다시 하기" : "터치해서 처음부터 다시 하기";
            GUI.Label(new Rect(w * .06f, h * .2f, w * .88f, h * .6f), (goal == Goal.Done ? ending : "거인에게 붙잡혔어요") + "\n\n" + again, style);
        }
        if (card != null)
        {
            GUI.color = new Color(0, 0, 0, .9f);
            GUI.DrawTexture(new Rect(0, 0, w, h), black);
            GUI.color = Color.white;
            GUI.Label(new Rect(w * .06f, h * .16f, w * .88f, h * .42f), card, style);
            if (choices == null) GUI.Label(new Rect(0, h * .84f, w, h * .05f), "터치해서 계속", small);
            else for (int i = 0; i < choices.Length; i++) Button(ChoiceRect(i), choices[i]);
        }
    }

    // -autotest 로 실행하면 규칙을 끝까지 확인하고 결과를 로그로 남긴다
    IEnumerator AutoTest()
    {
        void Put(Vector3 p) { player.enabled = false; player.transform.position = p; player.enabled = true; }
        void Check(string n, bool ok) => Debug.Log($"AUTOTEST {(ok ? "OK" : "FAIL")} {n}");
        string shot = null;
        var args = System.Environment.GetCommandLineArgs();
        int si = System.Array.IndexOf(args, "-shot");
        if (si >= 0) shot = args[si + 1];

        yield return new WaitForSeconds(.5f);
        eyeTimer = 99;
        Vector3 start = player.transform.position;
        simHold = true; yield return new WaitForSeconds(1); simHold = false;
        float walked = Vector3.Distance(start, player.transform.position);
        Check($"walk {walked:F2}m", walked > 1 && walked < 2.2f);

        Put(WinePos + Vector3.up * .6f); yield return null; yield return null;
        Check("wine picked with stones", goal == Goal.Stake && stones == 2);

        Put(new Vector3(-4, 1, -10.7f)); yaw = smoothYaw = 0; simHold = true;
        yield return new WaitForSeconds(.6f); simHold = false;
        Check("puddle wakes eye", eye == Eye.Open);
        eye = Eye.Closed; eyeTimer = 99; seen = 0; beam.enabled = false;

        Put(CrewPos[1] + Vector3.up + Vector3.left * .8f); yield return null; yield return null;
        Check("free button offered", act != null && act.Contains("에우릴로코스"));
        Free(1); Check("socks given", socks == 3);
        Put(new Vector3(-4, 1, -10.7f)); yaw = smoothYaw = 0; yield return null; yield return null;
        simHold = true; yield return new WaitForSeconds(.6f); simHold = false;
        Check("socks muffle puddle", eye == Eye.Closed && socks == 2);

        Put(StakePos + Vector3.up * .6f); yield return null; yield return null;
        Check("stake picked", goal == Goal.Harden);
        Put(FirePos + new Vector3(-1.5f, 1, 1)); yield return new WaitForSeconds(1);
        Check("not hard yet", goal == Goal.Harden && hardenT > .2f);
        yield return new WaitForSeconds(1.5f);
        Check("stake hardened", goal == Goal.Feed);

        Put(new Vector3(0, 1, -.6f)); yaw = 0; pitch = -12; yield return null; yield return null;
        Check("name asked", card != null && choices != null && eye == Eye.Open);
        Choose(0); yield return null;
        Check("wine sends him to sleep", nobody && eye == Eye.Closed && goal == Goal.Stab && card == null);
        yield return new WaitForSeconds(.3f);
        if (shot != null) ScreenCapture.CaptureScreenshot(shot);
        Check("can stab", canStab);
        Check("one tap not enough", act != null);
        for (int i = 0; i < 4; i++) { onAct(); }
        Check("mash builds", goal == Goal.Stab && stabPower > .7f);
        onAct(); if (goal == Goal.Stab) onAct();
        yield return null;
        Check("stabbed", goal == Goal.Sheep && boulder == null);

        stumbleT = 0; roamTo = new Vector3(0, 0, 13); yield return null;
        Vector3 g0 = giant.position; yield return new WaitForSeconds(.6f);
        Check("giant roams", Vector3.Distance(g0, giant.position) > .5f);
        Check("giant upright", Mathf.Approximately(giant.position.y, StandHeight));

        giant.position = new Vector3(-8, StandHeight, 12);
        Put(new Vector3(6, 1, 4)); yaw = smoothYaw = 90; yield return null;
        ThrowRock(); Check("stone thrown", stones == 1 && rockT >= 0);
        yield return new WaitForSeconds(1);
        Check("stone lures giant", giantAlert > 0 && Vector3.Distance(noisePos, new Vector3(6, 0, 4)) > 3);
        giantAlert = 0;

        giant.position = new Vector3(-8, StandHeight, 12);
        Vector3 me = new Vector3(6, 0, 4);
        float before = Vector3.Distance(new Vector3(-8, 0, 12), me);
        testing = false;
        Alert(me); yield return new WaitForSeconds(.5f);
        Check("rushes to sound", before - Vector3.Distance(new Vector3(giant.position.x, 0, giant.position.z), me) > 1.4f);
        hasFeather = true; Catch();
        Check("feather saves once", goal == Goal.Sheep && !hasFeather && safeT > 0);
        safeT = 0; Alert(me);
        yield return new WaitForSeconds(4.5f);
        Check("caught by sound", goal == Goal.Caught);
        testing = true; goal = Goal.Sheep; breath.Play();
        Put(sheep[1].position + Vector3.up); yield return null; yield return null;
        Check("sheep button offered", nearSheep != null && !hasSheep && act == "양 배에 매달리기");
        GripSheep(); yield return null;
        Check("hid under sheep", hasSheep);

        cheese = true; grip = .1f; EatCheese();
        Check("cheese restores grip", grip == 1 && !cheese);
        grip = .01f; simHold = true; yield return new WaitForSeconds(.5f); simHold = false;
        Check("grip slips", !hasSheep);
        Put(sheep[1].position + Vector3.up); yield return null; yield return null;
        GripSheep();

        testing = false; Put(new Vector3(0, 1, 11)); handTimer = 0; yield return new WaitForSeconds(1.5f);
        Check("hand comes down", handState == 2);
        Check("still is safe", goal == Goal.Sheep);
        stones = 1; ThrowRock(); yield return new WaitForSeconds(.9f);
        Check("stone pulls hand away", handState == 0);
        handState = 2; handTimer = 2; hand.gameObject.SetActive(true);
        simHold = true; yield return new WaitForSeconds(.4f); simHold = false;
        Check("moving under hand caught", goal == Goal.Caught);

        testing = true; goal = Goal.Sheep; handState = 0; handTimer = 99; hand.gameObject.SetActive(false);
        Put(new Vector3(0, 1, 17.4f)); yield return null; yield return null;
        Check("escaped asks last word", goal == Goal.Done && choices != null);
        Choose(1); yield return null;
        Check("ending written", ending.Contains("칭호"));

        // 잡힌 뒤 다시 시작하는 경로도 실제로 불러 본다
        saved = true; savedCrew = new[] { 2, 0, 0 }; savedStones = 2;
        SceneManager.LoadScene(0);
    }

    IEnumerator ResumeTest()
    {
        yield return new WaitForSeconds(.3f);
        Debug.Log($"AUTOTEST {(goal == Goal.Sheep && stones == 2 && !crew[0].gameObject.activeSelf && crew[1].gameObject.activeSelf ? "OK" : "FAIL")} resume after stab");
        Debug.Log("AUTOTEST END");
        yield return new WaitForSeconds(.3f);
        Application.Quit();
    }
}
