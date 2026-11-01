using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 장면 전체를 코드로 구성한다. 빈 씬의 오브젝트 하나에 붙이면 동작한다.
public class CyclopsGame : MonoBehaviour
{
    public enum Eye { Closed, Warning, Open }
    public enum Goal { Stake, Stab, Sheep, Done, Caught }

    const float WalkSpeed = 1.7f, SheepSpeed = 1.0f, ReachDist = 3f;
    const float StandHeight = 6.2f, RoamSpeed = 1.6f, RushSpeed = 4f, StumbleTime = 2.5f;
    static readonly Vector3 HeadPos = new Vector3(0, 1.4f, 2);
    static readonly Vector3 GatePos = new Vector3(0, 0, 15);
    static readonly Vector3 StakePos = new Vector3(8, 0.4f, -12);

    public Goal goal = Goal.Stake;
    public Eye eye = Eye.Closed;
    public bool simHold;
    public bool hasSheep;

    CharacterController player;
    Camera cam;
    Transform giant, body, armL, armR, lid, pupil, stake, sheepHeld;
    Transform[] sheep;
    Light beam, eyeGlow, torch;
    AudioSource breath, heart, sfx;
    AudioClip splash, crack, stabClip, stomp;
    Texture2D vignette, black;
    GUIStyle style;
    Vector3[] puddles = { new Vector3(-4, 0, -9), new Vector3(3, 0, -6), new Vector3(-6, 0, -2), new Vector3(5, 0, -1), new Vector3(0, 0, -11),
        new Vector3(-3, 0, 6), new Vector3(5, 0, 10), new Vector3(0, 0, 12), new Vector3(-9, 0, 13) };
    Vector3[] bones = { new Vector3(1, 0, -13), new Vector3(-8, 0, -6), new Vector3(9, 0, -5), new Vector3(-2, 0, -3), new Vector3(3, 0, 7), new Vector3(-7, 0, 8), new Vector3(8, 0, 13) };
    bool[] boneUsed;
    float yaw, pitch, eyeTimer = 5, sweepBase, seen, heartT, bob, breathT, giantAlert, stumbleT, stompT, armPhase, curSpeed, smoothYaw, stepT, grip = 1, handTimer = 3, msgT;
    bool scared, wasInPuddle, moving, canStab, testing;
    int handState;
    string msg = "";
    Transform nearSheep, hand;
    Vector3 lastMouse, noisePos, roamTo;

    void Start()
    {
        Application.targetFrameRate = 60;
        boneUsed = new bool[bones.Length];
        BuildCave();
        BuildGiant();
        BuildPlayer();
        BuildAudio();
        vignette = MakeVignette();
        black = Texture2D.whiteTexture;
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-autotest") >= 0)
            StartCoroutine(AutoTest());
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

        stake = Prim(PrimitiveType.Cylinder, StakePos, new Vector3(.14f, 1.1f, .14f), new Color(.42f, .25f, .1f), false).transform;
        stake.rotation = Quaternion.Euler(0, 0, 80);

        sheep = new Transform[3];
        var spots = new[] { new Vector3(-9, 0, 5), new Vector3(9, 0, 7), new Vector3(-5, 0, 10) };
        for (int i = 0; i < 3; i++) sheep[i] = MakeSheep(spots[i]);

        var door = new GameObject("Daylight").AddComponent<Light>();
        door.type = LightType.Point; door.color = new Color(.65f, .78f, 1); door.range = 11; door.intensity = 1.6f;
        door.transform.position = new Vector3(0, 3, 18.5f);
    }

    Transform MakeSheep(Vector3 at)
    {
        var s = new GameObject("Sheep").transform;
        s.position = at;
        Prim(PrimitiveType.Sphere, new Vector3(0, .7f, 0), new Vector3(1, .85f, 1.4f), new Color(.86f, .84f, .78f), false, s);
        Prim(PrimitiveType.Sphere, new Vector3(0, .95f, .75f), new Vector3(.42f, .42f, .5f), new Color(.15f, .13f, .12f), false, s);
        return s;
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

        beam = new GameObject("EyeBeam").AddComponent<Light>();
        beam.type = LightType.Spot; beam.spotAngle = 30; beam.range = 30; beam.intensity = 9;
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
        cam.transform.SetParent(go.transform, false);
        cam.transform.localPosition = Vector3.up * .65f;
        cam.backgroundColor = Color.black; cam.fieldOfView = 72; cam.nearClipPlane = .05f;
        cam.gameObject.AddComponent<AudioListener>();
        torch = cam.gameObject.AddComponent<Light>();
        torch.type = LightType.Point; torch.range = 7; torch.intensity = .6f; torch.color = new Color(1, .75f, .55f);
        yaw = smoothYaw = 35;
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

    void Update()
    {
        if (goal == Goal.Done || goal == Goal.Caught)
        {
            if (Input.GetMouseButtonDown(0) && !testing) SceneManager.LoadScene(0);
            return;
        }
        Move();
        Objectives();
        EyeLogic();
        Feel();
    }

    Rect StabRect => new Rect(Screen.width * .2f, Screen.height * .72f, Screen.width * .6f, Screen.height * .12f);

    void Move()
    {
        Vector2 gui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (canStab && Input.GetMouseButtonDown(0) && StabRect.Contains(gui)) { Stab(); return; }
        if (nearSheep && Input.GetMouseButtonDown(0) && StabRect.Contains(gui)) { GripSheep(); return; }

        // 왼쪽 절반을 누르면 걷고 오른쪽 절반을 끌면 둘러본다. 두 손가락을 함께 쓸 수 있다
        bool walk = simHold || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        Vector2 look = Vector2.zero;
        if (Input.touchCount > 0)
        {
            foreach (var t in Input.touches)
                if (t.position.x < Screen.width * .5f) walk = true;
                else if (t.phase == TouchPhase.Moved) look += t.deltaPosition;
        }
        else
        {
            if (Input.GetMouseButtonDown(0)) lastMouse = Input.mousePosition;
            if (Input.GetMouseButton(0))
            {
                if (lastMouse.x < Screen.width * .5f) walk = true;
                else look = Input.mousePosition - lastMouse;
                if (!walk) lastMouse = Input.mousePosition;
            }
        }
        float scale = 900f / Mathf.Max(Screen.height, 1);
        yaw += look.x * .22f * scale;
        pitch = Mathf.Clamp(pitch - look.y * .16f * scale, -50, 50);
        // 가볍게 출발하고 손을 떼면 바로 멈춘다
        float wanted = walk ? (hasSheep ? SheepSpeed : WalkSpeed) : 0;
        curSpeed = Mathf.MoveTowards(curSpeed, wanted, Time.deltaTime * (walk ? 6 : 14));
        moving = curSpeed > .25f;
        smoothYaw = Mathf.LerpAngle(smoothYaw, yaw, 1 - Mathf.Exp(-18 * Time.deltaTime));
        cam.transform.localRotation = Quaternion.Euler(pitch, 0, Mathf.Clamp(Mathf.DeltaAngle(yaw, smoothYaw) * .2f, -4, 4));
        player.transform.rotation = Quaternion.Euler(0, smoothYaw, 0);
        Vector3 v = player.transform.forward * curSpeed;
        player.Move((v + Vector3.down * 3) * Time.deltaTime);
        bob += Time.deltaTime * curSpeed * 4.2f;
        stepT += Time.deltaTime * curSpeed;
        if (stepT > .85f) { stepT = 0; sfx.PlayOneShot(crack, .07f); }
        float camY = hasSheep ? -.55f : .65f;
        cam.transform.localPosition = Vector3.up * (camY + Mathf.Sin(bob) * .025f);
        if (sheepHeld)
        {
            sheepHeld.position = player.transform.position + Vector3.down * .72f + player.transform.forward * .1f;
            sheepHeld.rotation = player.transform.rotation;
        }

        // 소리: 물웅덩이와 뼈다귀. 양 밑에 숨어 있으면 들키지 않는다
        if (hasSheep) return;
        var flat = player.transform.position; flat.y = 0;
        bool inPuddle = false;
        foreach (var p in puddles) inPuddle |= Vector3.Distance(flat, p) < 1.3f;
        if (inPuddle && !wasInPuddle && moving) { sfx.PlayOneShot(splash); Alert(flat); }
        wasInPuddle = inPuddle;
        for (int i = 0; i < bones.Length; i++)
            if (!boneUsed[i] && Vector3.Distance(flat, bones[i]) < .7f) { boneUsed[i] = true; sfx.PlayOneShot(crack); Alert(flat); }
    }

    void Alert(Vector3 at)
    {
        if (goal == Goal.Sheep) { giantAlert = 6; noisePos = at; return; }
        Vector3 dir = at - HeadPos;
        sweepBase = Mathf.Atan2(dir.x, -dir.z) * Mathf.Rad2Deg;
        eye = Eye.Open; eyeTimer = 3.5f;
    }

    void Objectives()
    {
        var pos = player.transform.position;
        if (goal == Goal.Stake && Vector3.Distance(pos, StakePos) < 1.6f)
        {
            goal = Goal.Stab;
            stake.SetParent(cam.transform, false);
            stake.localPosition = new Vector3(.32f, -.3f, .6f);
            stake.localRotation = Quaternion.Euler(70, 0, 0);
            stake.localScale = new Vector3(.05f, .4f, .05f);
        }
        canStab = goal == Goal.Stab && eye == Eye.Closed && scared && Vector3.Distance(pos, HeadPos) < ReachDist;
        // 처음 얼굴 앞에 서는 순간 눈이 번쩍 뜨인다
        if (goal == Goal.Stab && !scared && Vector3.Distance(pos, HeadPos) < ReachDist)
        {
            scared = true; eye = Eye.Open; eyeTimer = 1.3f;
            Vector3 d = pos - HeadPos; sweepBase = Mathf.Atan2(d.x, -d.z) * Mathf.Rad2Deg;
        }
        if (goal == Goal.Sheep)
        {
            nearSheep = null;
            if (!hasSheep)
                foreach (var one in sheep)
                    if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), one.position) < 1.8f) nearSheep = one;
            GiantRoam(new Vector3(pos.x, 0, pos.z));
            if (hasSheep && pos.z > 17 && Mathf.Abs(pos.x) < 3.2f) goal = Goal.Done;
        }
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
        float speed = hears ? RushSpeed : RoamSpeed;
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
        else if (handState == 2 && handTimer <= 0) { handState = 0; handTimer = Random.Range(3f, 5f); hand.gameObject.SetActive(false); }
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

    public void Stab()
    {
        goal = Goal.Sheep; canStab = false; eye = Eye.Closed;
        sfx.PlayOneShot(stabClip);
        lid.gameObject.SetActive(false);
        pupil.GetComponent<Renderer>().material.color = new Color(.6f, 0, 0);
        stake.SetParent(giant, false);
        stake.localPosition = new Vector3(0, .3f, -1.8f);
        stake.localRotation = Quaternion.Euler(90, 0, 0);
        // 누워 있던 몸을 머리 아래로 세운다
        body.localPosition = new Vector3(0, -3.6f, 0);
        body.localRotation = Quaternion.identity;
        body.localScale = new Vector3(4.2f, 2.6f, 3);
        armL.gameObject.SetActive(true); armR.gameObject.SetActive(true);
        stumbleT = StumbleTime;
        roamTo = new Vector3(0, 0, 10);
        Say("거인은 입구를 지나는 것을 손으로 더듬어요\n양 배에 매달려야 빠져나갈 수 있어요", 6);
        beam.enabled = eyeGlow.enabled = false;
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
        else if (eye == Eye.Open && eyeTimer <= 0) { eye = Eye.Closed; eyeTimer = Random.Range(7f, 12f); seen = 0; }

        lid.gameObject.SetActive(eye != Eye.Open);
        beam.enabled = eyeGlow.enabled = eye == Eye.Open;
        // 숨소리가 끊기면 곧 눈이 뜨인다는 신호
        breath.volume = Mathf.MoveTowards(breath.volume, eye == Eye.Closed ? 1 : 0, Time.deltaTime * 3);
        if (eye != Eye.Open) return;

        float look = sweepBase + Mathf.Sin(Time.time * .9f) * 22;
        Quaternion q = Quaternion.Euler(4, 180 - look, 0);
        beam.transform.rotation = q;
        pupil.localPosition = new Vector3(Mathf.Clamp(look / 90f, -1, 1) * -.3f, .3f, -1.47f);

        Vector3 from = beam.transform.position, to = cam.transform.position - from;
        bool inCone = Vector3.Angle(q * Vector3.forward, to) < beam.spotAngle * .5f;
        bool clear = !Physics.Raycast(from, to.normalized, out var hit, to.magnitude) || hit.collider == player;
        // 빛 속에서 움직이면 들킨다. 멈추면 살 수 있다
        seen = inCone && clear && moving ? seen + Time.deltaTime : 0;
        if (seen > .3f) Catch();
    }

    void Catch() { if (testing) return; goal = Goal.Caught; breath.Stop(); }

    void Feel()
    {
        float d = Vector3.Distance(player.transform.position, giant.position);
        float near = Mathf.InverseLerp(20, 3, d);
        breathT = Mathf.Lerp(breathT, near, Time.deltaTime * 2);
        heartT -= Time.deltaTime;
        if (heartT <= 0) { heart.volume = .35f + near * .65f; heart.Play(); heartT = Mathf.Lerp(1.1f, .42f, near); }
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
            style.font = Font.CreateDynamicFontFromOSFont(new[] { "Apple SD Gothic Neo", "Noto Sans CJK KR", "NotoSansCJK-Regular", "Arial" }, 32);
            style.normal.textColor = new Color(.96f, .9f, .77f);
        }
        float h = Screen.height, w = Screen.width;
        style.fontSize = Mathf.RoundToInt(h * .026f);
        float a = Mathf.Clamp01(.35f + breathT * .65f);
        GUI.color = new Color(1, 1, 1, a);
        GUI.DrawTexture(new Rect(0, 0, w, h), vignette, ScaleMode.StretchToFill);
        GUI.color = Color.white;

        string line = goal switch
        {
            Goal.Stake => "모닥불 옆의 말뚝을 찾으세요",
            Goal.Stab => eye == Eye.Open ? "움직이지 마세요" : "잠든 거인의 눈앞으로",
            Goal.Sheep => hasSheep ? "천천히 동굴 입구로" : player.transform.position.z > 15 ? "양 없이는 나갈 수 없어요" : "소리 내지 말고 양 밑에 숨으세요",
            _ => ""
        };
        if (goal <= Goal.Sheep) GUI.Label(new Rect(0, h * .05f, w, h * .06f), line, style);
        if (goal == Goal.Stake && Time.timeSinceLevelLoad < 10)
        {
            GUI.Label(new Rect(0, h * .82f, w * .5f, h * .1f), "왼쪽 누르기\n걷기", style);
            GUI.Label(new Rect(w * .5f, h * .82f, w * .5f, h * .1f), "오른쪽 끌기\n둘러보기", style);
        }
        msgT -= Time.deltaTime;
        if (msgT > 0 && goal <= Goal.Sheep) GUI.Label(new Rect(0, h * .13f, w, h * .12f), msg, style);
        if (hasSheep && goal == Goal.Sheep)
        {
            GUI.color = grip < .3f ? new Color(.9f, .2f, .15f) : new Color(.93f, .85f, .6f);
            GUI.DrawTexture(new Rect(w * .2f, h * .92f, w * .6f * grip, h * .012f), black);
            GUI.color = Color.white;
        }
        if (nearSheep && GUI.Button(StabRect, "양 배에 매달리기", style)) GripSheep();
        if (canStab && GUI.Button(StabRect, "눈을 찌른다", style)) Stab();
        if (goal >= Goal.Done)
        {
            GUI.color = new Color(0, 0, 0, .8f);
            GUI.DrawTexture(new Rect(0, 0, w, h), black);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, h * .35f, w, h * .3f), (goal == Goal.Done ? "동굴을 빠져나왔어요!" : "거인에게 붙잡혔어요") + "\n\n터치해서 다시 하기", style);
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

        testing = true;
        yield return new WaitForSeconds(.5f);
        eyeTimer = 99;
        Vector3 start = player.transform.position;
        simHold = true; yield return new WaitForSeconds(1); simHold = false;
        float walked = Vector3.Distance(start, player.transform.position);
        Check($"walk {walked:F2}m", walked > 1 && walked < 2.2f);

        Put(new Vector3(-4, 1, -9.5f)); yaw = 180; simHold = true;
        yield return new WaitForSeconds(.6f); simHold = false;
        Check("puddle wakes eye", eye == Eye.Open);
        eye = Eye.Closed; eyeTimer = 99; seen = 0; beam.enabled = false;

        Put(StakePos + Vector3.up * .6f); yield return null; yield return null;
        Check("stake picked", goal == Goal.Stab);

        Put(new Vector3(0, 1, -.6f)); yaw = 0; pitch = -12; yield return null; yield return null;
        Check("eye snaps open", eye == Eye.Open);
        yield return new WaitForSeconds(.5f);
        if (shot != null) ScreenCapture.CaptureScreenshot(shot);
        yield return new WaitForSeconds(1.2f);
        Check("still survives", goal == Goal.Stab);
        eyeTimer = 99; yield return null;
        Check("can stab", canStab);
        Stab();
        Check("stabbed", goal == Goal.Sheep);

        stumbleT = 0; roamTo = new Vector3(0, 0, 13); yield return null;
        Vector3 g0 = giant.position; yield return new WaitForSeconds(.6f);
        Check("giant roams", Vector3.Distance(g0, giant.position) > .5f);
        Check("giant upright", Mathf.Approximately(giant.position.y, StandHeight));

        giant.position = new Vector3(-8, StandHeight, 12);
        Put(new Vector3(6, 1, 4));
        Vector3 me = new Vector3(6, 0, 4);
        float before = Vector3.Distance(new Vector3(-8, 0, 12), me);
        testing = false;
        Alert(me); yield return new WaitForSeconds(.5f);
        Check("rushes to sound", before - Vector3.Distance(new Vector3(giant.position.x, 0, giant.position.z), me) > 1.4f);
        yield return new WaitForSeconds(4.5f);
        Check("caught by sound", goal == Goal.Caught);
        testing = true; goal = Goal.Sheep; breath.Play();
        Put(sheep[1].position + Vector3.up); yield return null; yield return null;
        Check("sheep button offered", nearSheep != null && !hasSheep);
        GripSheep(); yield return null;
        Check("hid under sheep", hasSheep);

        grip = .01f; simHold = true; yield return new WaitForSeconds(.5f); simHold = false;
        Check("grip slips", !hasSheep);
        Put(sheep[1].position + Vector3.up); yield return null; yield return null;
        GripSheep();

        testing = false; Put(new Vector3(0, 1, 11)); handTimer = 0; yield return new WaitForSeconds(1.5f);
        Check("hand comes down", handState == 2);
        Check("still is safe", goal == Goal.Sheep);
        simHold = true; yield return new WaitForSeconds(.4f); simHold = false;
        Check("moving under hand caught", goal == Goal.Caught);

        testing = true; goal = Goal.Sheep; handState = 0; handTimer = 99; hand.gameObject.SetActive(false);
        Put(new Vector3(0, 1, 17.4f)); yield return null; yield return null;
        Check("escaped", goal == Goal.Done);

        Debug.Log("AUTOTEST END");
        yield return new WaitForSeconds(.3f);
        Application.Quit();
    }
}
