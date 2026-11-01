using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 장면 전체를 코드로 구성한다. 빈 씬의 오브젝트 하나에 붙이면 동작한다.
public class CyclopsGame : MonoBehaviour
{
    public enum Eye { Closed, Warning, Open }
    public enum Goal { Stake, Stab, Sheep, Done, Caught }

    const float WalkSpeed = 1.7f, SheepSpeed = 1.0f, ReachDist = 3f;
    static readonly Vector3 HeadPos = new Vector3(0, 1.4f, 2);
    static readonly Vector3 GuardPos = new Vector3(0, 0, 13);
    static readonly Vector3 StakePos = new Vector3(8, 0.4f, -12);

    public Goal goal = Goal.Stake;
    public Eye eye = Eye.Closed;
    public bool simHold;
    public bool hasSheep;

    CharacterController player;
    Camera cam;
    Transform giant, lid, pupil, stake, sheepHeld;
    Transform[] sheep;
    Light beam, eyeGlow, torch;
    AudioSource breath, heart, sfx;
    AudioClip splash, crack, stabClip;
    Texture2D vignette, black;
    GUIStyle style;
    Vector3[] puddles = { new Vector3(-4, 0, -9), new Vector3(3, 0, -6), new Vector3(-6, 0, -2), new Vector3(5, 0, -1), new Vector3(0, 0, -11) };
    Vector3[] bones = { new Vector3(1, 0, -13), new Vector3(-8, 0, -6), new Vector3(9, 0, -5), new Vector3(-2, 0, -3) };
    bool[] boneUsed;
    float yaw, pitch, eyeTimer = 5, sweepBase, seen, heartT, bob, breathT, giantAlert;
    bool scared, wasInPuddle, moving, canStab;
    Vector3 lastMouse;

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
        Prim(PrimitiveType.Capsule, new Vector3(0, -.1f, 5), new Vector3(4.2f, 5, 3), skin, false, giant).transform.localRotation = Quaternion.Euler(90, 0, 0);
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
        yaw = 35;
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
            if (Input.GetMouseButtonDown(0) && !simHold) SceneManager.LoadScene(0);
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
        bool down = Input.GetMouseButton(0);
        Vector2 gui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (canStab && Input.GetMouseButtonDown(0) && StabRect.Contains(gui)) { Stab(); return; }
        if (Input.GetMouseButtonDown(0)) lastMouse = Input.mousePosition;
        if (down)
        {
            Vector3 d = Input.mousePosition - lastMouse;
            lastMouse = Input.mousePosition;
            float scale = 900f / Mathf.Max(Screen.height, 1);
            yaw += d.x * .22f * scale;
            pitch = Mathf.Clamp(pitch - d.y * .16f * scale, -50, 50);
        }
        moving = down || simHold;
        float speed = hasSheep ? SheepSpeed : WalkSpeed;
        cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
        player.transform.rotation = Quaternion.Euler(0, yaw, 0);
        Vector3 v = moving ? player.transform.forward * speed : Vector3.zero;
        player.Move((v + Vector3.down * 3) * Time.deltaTime);
        if (moving) bob += Time.deltaTime * 7;
        float camY = hasSheep ? .05f : .65f;
        cam.transform.localPosition = Vector3.up * (camY + Mathf.Sin(bob) * .025f);
        if (sheepHeld)
        {
            sheepHeld.position = player.transform.position + Vector3.down * .9f + player.transform.forward * .3f;
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
        if (goal == Goal.Sheep) { giantAlert = 5; return; }
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
            if (!hasSheep)
                foreach (var s in sheep)
                    if (Vector3.Distance(new Vector3(pos.x, 0, pos.z), s.position) < 1.8f) { hasSheep = true; sheepHeld = s; break; }
            giantAlert -= Time.deltaTime;
            Vector3 t = !hasSheep && giantAlert > 0 ? new Vector3(pos.x, 0, pos.z) : GuardPos;
            giant.position = Vector3.MoveTowards(giant.position, t, 2f * Time.deltaTime);
            var flat = giant.position; flat.y = 0;
            if (!hasSheep && Vector3.Distance(flat, new Vector3(pos.x, 0, pos.z)) < 3.5f) Catch();
            if (hasSheep && pos.z > 17 && Mathf.Abs(pos.x) < 3.2f) goal = Goal.Done;
        }
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
        giant.position = GuardPos;
        giant.rotation = Quaternion.Euler(-70, 0, 0);
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

    void Catch() { goal = Goal.Caught; breath.Stop(); }

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
            Goal.Sheep => hasSheep ? "천천히 동굴 입구로" : "양 밑에 숨으세요",
            _ => ""
        };
        if (goal <= Goal.Sheep) GUI.Label(new Rect(0, h * .05f, w, h * .06f), line, style);
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

        Put(new Vector3(0, 1, 16)); yield return new WaitForSeconds(.2f);
        Check("no sheep caught at exit", goal == Goal.Caught);
        goal = Goal.Sheep; breath.Play();
        Put(sheep[1].position + Vector3.up); yield return null; yield return null;
        Check("hid under sheep", hasSheep);
        Put(new Vector3(0, 1, 17.4f)); yield return null; yield return null;
        Check("escaped", goal == Goal.Done);

        Debug.Log("AUTOTEST END");
        yield return new WaitForSeconds(.3f);
        Application.Quit();
    }
}
