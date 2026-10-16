using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;

/// <summary>
/// One-click foot IK. Finds the legs of a two- or four-legged character from its skinned
/// skeleton, builds the FootRig under its Animator (per leg: a foot reference, a two-bone IK and
/// a raycast foot) and wires ArmatureIK to it.
///
/// Run it on a prefab asset in the Project window, a character in the scene, or one open in
/// Prefab Mode. Running it again rebuilds the rig and keeps the settings already tuned on it.
/// </summary>
public static class FootIKAutoSetup
{
    private const string RigName = "FootRig";
    private const string Title = "Foot IK Auto Setup";

    // A bone joins the segment below it when they bend by less than this and share a name:
    // Rigify and similar rigs split one limb segment into several colinear bones.
    private const float ColinearAngle = 12f;

    // A segment ending at the lowest bone that lies flatter than this is a toe on a flat foot,
    // so the leg's IK tip is the joint above it (a biped's ankle, not its toes).
    private const float FlatFootAngle = 50f;

    // A chain whose lower bone is shorter than this share of its upper bone ends one joint too low.
    private const float LopsidedRatio = 0.4f;

    private struct Leg
    {
        public string label, suffix;
        public Transform root, mid, tip;

        // Where the leg joins the body: classifies the leg front/hind and left/right, as the
        // feet themselves may be mid-stride in the default pose.
        public Transform anchor;
    }

    [MenuItem("GameObject/Inverse Kinematics/Auto Setup Foot IK", false, 0)]
    [MenuItem("Tools/Inverse Kinematics/Auto Setup Foot IK")]
    private static void SetupSelected() => Run(Selection.activeGameObject);

    [MenuItem("GameObject/Inverse Kinematics/Auto Setup Foot IK", true)]
    [MenuItem("Tools/Inverse Kinematics/Auto Setup Foot IK", true)]
    private static bool CanSetupSelected() => Selection.activeGameObject != null;

    /// <summary>Builds the foot IK for the character <paramref name="selected"/> belongs to.</summary>
    public static void Run(GameObject selected)
    {
        if (selected == null)
            return;

        pendingMasks.Clear();
        string assetPath = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(assetPath))
        {
            if (Setup(selected, true))
                MaskArmatureOutOfModels();
            return;
        }

        if (PrefabUtility.GetPrefabAssetType(selected) == PrefabAssetType.Model)
        {
            EditorUtility.DisplayDialog(Title, "This is a model, not a prefab. Make a prefab of it first, then run the setup on that.", "OK");
            return;
        }

        // A prefab asset picked in the Project window: edit its contents and save them back.
        GameObject contents = PrefabUtility.LoadPrefabContents(assetPath);
        bool built = false;
        try
        {
            built = Setup(contents, false);
            if (built)
                PrefabUtility.SaveAsPrefabAsset(contents, assetPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
        // Reimports the model, so only once the prefab built from it is saved and closed.
        if (built)
            MaskArmatureOutOfModels();
    }

    /// <summary>Builds the rig on <paramref name="selected"/> in place, without saving any asset.</summary>
    public static bool Setup(GameObject selected, bool selectRig)
    {
        // Unity objects, so no ??: a missing component can be a fake null in the editor.
        Animator animator = selected.GetComponentInChildren<Animator>(true);
        if (animator == null)
            animator = selected.GetComponentInParent<Animator>(true);
        if (animator == null)
            return Fail("No Animator found on or under " + selected.name + ".");

        NavMeshAgent agent = animator.GetComponentInParent<NavMeshAgent>(true);
        if (agent == null)
            agent = selected.GetComponentInChildren<NavMeshAgent>(true);
        Rigidbody body = null;
        if (agent == null)
        {
            body = animator.GetComponentInParent<Rigidbody>(true);
            if (body == null)
                body = selected.GetComponentInChildren<Rigidbody>(true);
        }
        Transform character = agent != null ? agent.transform : body != null ? body.transform : animator.transform;

        Transform oldRig = animator.transform.Find(RigName);
        bool rerun = oldRig != null;
        var renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var bones = new HashSet<Transform>(renderers
            .SelectMany(DeformingBones)
            .Where(b => b != null && b.IsChildOf(animator.transform) && (oldRig == null || !b.IsChildOf(oldRig))));
        if (bones.Count == 0)
            return Fail("No skinned bones found under " + animator.name + ".");

        Transform armature = ArmatureParent(bones, animator.transform);
        if (armature == null)
            return Fail("The skinned bones do not share one armature object under " + animator.name + ".");

        // The character's root stands on the ground (a NavMeshAgent's raised by its base offset).
        // Not the renderers' bounds: imported skinned bounds can be far off.
        float groundY = character.position.y - (agent != null ? agent.baseOffset : 0f);

        List<Leg> legs = animator.isHuman ? HumanLegs(animator) : FindLegs(bones, groundY);
        if (!Classify(legs, character, out string problem))
            return Fail(problem);

        // Scales the ray lengths and reach. Measured along the bones, not as height off the
        // ground, so a crouched or oddly posed default pose does not shrink it.
        float legLength = Mathf.Max(0.05f, legs.Average(l =>
            Vector3.Distance(l.root.position, l.mid.position) + Vector3.Distance(l.mid.position, l.tip.position)));

        // How far the IK tip sits above the sole. The model's default pose may be mid-stride with
        // feet lifted, so the lowest foot is the one standing on the ground.
        float plantedYOffset = Mathf.Max(0f, legs.Min(l => l.tip.position.y) - groundY);

        // Per leg, from the bones below each foot. A pair shares the taller one: a foot lifted
        // mid-stride bends its pastern back and measures short.
        var soleHeight = new Dictionary<Transform, float>();
        foreach (var pair in legs.GroupBy(l => l.label.StartsWith("Hind") ? "Hind" : "Front"))
        {
            float height = pair.Max(l => PlantedHeight(l.tip, plantedYOffset));
            foreach (Leg l in pair)
                soleHeight[l.tip] = height;
        }

        int hitMask = LayerMask.GetMask("TerrainLayer", "IK");
        if (hitMask == 0)
            hitMask = Physics.DefaultRaycastLayers;
        int ownMask = character.gameObject.layer == 0 ? 0 : 1 << character.gameObject.layer;

        Undo.SetCurrentGroupName(Title);
        int undoGroup = Undo.GetCurrentGroup();

        // Settings already tuned on a previous rig are carried over, so a rerun only rewires it.
        var oldFeet = oldRig != null
            ? oldRig.GetComponentsInChildren<FootIKScript>(true).ToDictionary(f => f.name, f => f)
            : new Dictionary<string, FootIKScript>();
        var keptFeet = oldFeet.ToDictionary(p => p.Key, p => Snapshot(p.Value));

        RigBuilder builder = animator.GetComponent<RigBuilder>();
        if (builder == null)
            builder = Undo.AddComponent<RigBuilder>(animator.gameObject);
        Undo.RecordObject(builder, Title);

        int layerIndex = builder.layers.FindIndex(l => l.rig != null && oldRig != null && l.rig.transform == oldRig);
        builder.layers.RemoveAll(l => l.rig == null || (oldRig != null && l.rig.transform == oldRig));
        if (oldRig != null)
            Undo.DestroyObjectImmediate(oldRig.gameObject);

        var rigObject = new GameObject(RigName);
        Undo.RegisterCreatedObjectUndo(rigObject, Title);
        rigObject.layer = animator.gameObject.layer;
        rigObject.transform.SetParent(animator.transform, false);
        Rig rig = rigObject.AddComponent<Rig>();

        var layer = new RigLayer(rig);
        if (layerIndex >= 0 && layerIndex <= builder.layers.Count)
            builder.layers.Insert(layerIndex, layer);
        else
            builder.layers.Add(layer);

        ArmatureIK armatureIK = character.GetComponentsInChildren<ArmatureIK>(true).FirstOrDefault();
        // Its settings are kept on a rerun only: an ArmatureIK added by hand before any rig existed
        // still has its untuned defaults (no hit layers at all).
        bool tuneArmatureIK = armatureIK == null || !rerun;
        if (armatureIK == null)
            armatureIK = Undo.AddComponent<ArmatureIK>(animator.gameObject);

        // Rig constraints run in hierarchy order: every foot reference has to read the animated
        // foot before any IK moves it, so they all come first.
        var refs = new MultiParentConstraint[legs.Count];
        for (int i = 0; i < legs.Count; i++)
            refs[i] = FootRef(rigObject.transform, legs[i]);

        var report = new StringBuilder();
        report.AppendLine($"{Title}: {legs.Count} legs on {character.name}, armature '{armature.name}'.");
        for (int i = 0; i < legs.Count; i++)
        {
            Leg leg = legs[i];
            TwoBoneIKConstraint ik = FootIK(rigObject.transform, leg, character, out Transform target);

            var foot = Child(rigObject.transform, "RaycastDown" + leg.suffix).AddComponent<FootIKScript>();
            var so = new SerializedObject(foot);
            // The same foot's old settings, else any old foot's (the old rig may have named them differently).
            if (keptFeet.TryGetValue(foot.name, out var kept) || (kept = keptFeet.Values.FirstOrDefault()) != null)
            {
                Restore(so, kept);
                // That clearance was measured for the old foot bone: a new one sits at another height.
                if (!ReferenceEquals(kept[OldTipKey], leg.tip))
                    so.FindProperty("plantedYOffset").floatValue = soleHeight[leg.tip];
            }
            else
            {
                so.FindProperty("rayYOffset").floatValue = legLength * 0.6f;
                so.FindProperty("rayDistance").floatValue = legLength * 0.6f;
                so.FindProperty("plantedYOffset").floatValue = soleHeight[leg.tip];
                so.FindProperty("ExitIKThreashhold").floatValue = legLength * 0.3f;
                so.FindProperty("ToIkSpeed").floatValue = 30f;
                so.FindProperty("ToNoIkSpeed").floatValue = 30f;
                so.FindProperty("IKFootRotationSpeed").floatValue = 15f;
                so.FindProperty("mask").intValue = hitMask;
                so.FindProperty("playerLayer").intValue = ownMask;
            }
            so.FindProperty("footRefConstraint").objectReferenceValue = refs[i];
            so.FindProperty("footIK").objectReferenceValue = ik;
            so.FindProperty("IKTarget").objectReferenceValue = target;
            so.FindProperty("ArmatureIKScript").objectReferenceValue = armatureIK;
            so.ApplyModifiedPropertiesWithoutUndo();

            report.AppendLine($"  {leg.label}: {leg.root.name} > {leg.mid.name} > {leg.tip.name}");
        }

        Transform Ref(string suffix) =>
            refs.FirstOrDefault(r => r.name == "FootRef" + suffix)?.transform;

        bool fourLegged = legs.Count == 4;
        var arm = new SerializedObject(armatureIK);
        arm.FindProperty("SelectedOption").enumValueIndex = (int)(body != null ? ArmatureIK.OptionType.Rigidbody : ArmatureIK.OptionType.NavMeshAgent);
        arm.FindProperty("UsingNavMeshAgent").boolValue = body == null;
        arm.FindProperty("UsingRigidbody").boolValue = body != null;
        arm.FindProperty("navAgent").objectReferenceValue = agent;
        arm.FindProperty("characterRigidBody").objectReferenceValue = body;
        arm.FindProperty("rightFoot").objectReferenceValue = Ref(fourLegged ? "FR" : "RF");
        arm.FindProperty("leftFoot").objectReferenceValue = Ref(fourLegged ? "FL" : "LF");
        arm.FindProperty("hindRightFoot").objectReferenceValue = fourLegged ? Ref("HR") : null;
        arm.FindProperty("hindLeftFoot").objectReferenceValue = fourLegged ? Ref("HL") : null;
        arm.FindProperty("armatureParent").objectReferenceValue = armature;
        arm.FindProperty("UpdateHeight").boolValue = true;
        arm.FindProperty("manualUpdateToggle").boolValue = true;
        // Its ray starts rayYOffset above each foot and is rayDistance long, so it has to be the
        // longer of the two to reach the ground under the foot at all.
        bool armatureBroken = arm.FindProperty("hitLayers").intValue == 0
            || arm.FindProperty("rayDistance").floatValue <= arm.FindProperty("rayYOffset").floatValue;
        if (tuneArmatureIK || armatureBroken)
        {
            arm.FindProperty("rayYOffset").floatValue = legLength * 0.6f;
            arm.FindProperty("rayDistance").floatValue = legLength * 1.2f;
            arm.FindProperty("AdjustSpeed").floatValue = 10f;
            arm.FindProperty("hitLayers").intValue = hitMask;
            arm.FindProperty("ignoreLayers").intValue = ownMask;
        }
        arm.ApplyModifiedProperties();

        FindArmatureCurves(animator, armature, report);

        if (agent == null && body == null)
            report.AppendLine("  Warning: no NavMeshAgent or Rigidbody found, so ArmatureIK will not move the body.");
        if (Mathf.Abs(armature.position.y - groundY) > legLength * 0.15f)
            report.AppendLine($"  Warning: '{armature.name}' sits {armature.position.y - groundY:0.00} m off the ground; ArmatureIK puts its origin on the ground.");

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log(report.ToString().TrimEnd(), rigObject);
        if (selectRig)
            Selection.activeGameObject = rigObject;
        return true;
    }

    // The object that moves the whole skeleton: the bones' ancestor that is a direct child of the Animator.
    private static Transform ArmatureParent(HashSet<Transform> bones, Transform animatorRoot)
    {
        Transform found = null;
        foreach (Transform bone in bones)
        {
            Transform t = bone;
            while (t.parent != null && t.parent != animatorRoot)
                t = t.parent;
            if (t.parent != animatorRoot)
                continue;
            if (found != null && found != t)
                return null;
            found = t;
        }
        return found;
    }

    private static List<Leg> HumanLegs(Animator animator)
    {
        var legs = new List<Leg>();
        void Add(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot)
        {
            Transform root = animator.GetBoneTransform(upper), mid = animator.GetBoneTransform(lower), tip = animator.GetBoneTransform(foot);
            if (root != null && mid != null && tip != null)
                legs.Add(new Leg { root = root, mid = mid, tip = tip, anchor = root });
        }
        Add(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        Add(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        return legs;
    }

    // Legs of a generic skeleton: the lowest-hanging leaf bones, each with the two joints above it.
    private static List<Leg> FindLegs(HashSet<Transform> bones, float groundY)
    {
        var hasChildBone = new HashSet<Transform>();
        foreach (Transform bone in bones)
            for (Transform t = bone.parent; t != null; t = t.parent)
                if (bones.Contains(t) && !hasChildBone.Add(t))
                    break;

        float height = bones.Max(b => b.position.y) - groundY;
        var tips = bones
            .Where(b => !hasChildBone.Contains(b) && b.position.y - groundY <= height * 0.3f)
            .OrderBy(b => b.position.y);

        var legs = new List<Leg>();
        foreach (Transform leaf in tips)
        {
            var joints = Joints(leaf, bones);
            if (joints.Count < 3)
                continue;

            int tip = Vector3.Angle(joints[0].position - joints[1].position, Vector3.down) > FlatFootAngle ? 1 : 0;

            // A short end segment (a pastern, a toe) under a long one makes a lopsided chain: the
            // IK bends at that small joint and swings the long bone around. The deer's front
            // "foot" is such a pastern, so its front legs bend at the knee one joint up instead.
            if (joints.Count >= tip + 4
                && Vector3.Distance(joints[tip].position, joints[tip + 1].position)
                   < LopsidedRatio * Vector3.Distance(joints[tip + 1].position, joints[tip + 2].position))
                tip++;

            if (joints.Count < tip + 3)
                continue;

            var leg = new Leg
            {
                tip = joints[tip], mid = joints[tip + 1], root = joints[tip + 2],
                anchor = joints[Mathf.Min(tip + 3, joints.Count - 1)],
            };

            // A dew claw or second toe hangs off the same leg: keep only the lowest.
            if (legs.Any(l => l.root == leg.root || l.mid == leg.mid || l.tip == leg.tip))
                continue;
            legs.Add(leg);
        }
        return legs;
    }

    // The leaf, then the head of every limb segment above it, with colinear bones merged.
    private static List<Transform> Joints(Transform leaf, HashSet<Transform> bones)
    {
        var joints = new List<Transform> { leaf };
        Transform below = leaf;
        Vector3? lastDir = null;
        for (Transform t = leaf.parent; t != null; t = t.parent)
        {
            if (!bones.Contains(t))
                continue;
            Vector3 dir = below.position - t.position;
            below = t;
            if (dir.sqrMagnitude < 1e-8f)
                continue;
            if (lastDir.HasValue && joints.Count > 1 && SameSegment(t, joints[joints.Count - 1], Vector3.Angle(dir, lastDir.Value)))
                joints[joints.Count - 1] = t;
            else
                joints.Add(t);
            lastDir = dir;
            if (joints.Count > 4)
                break;
        }
        return joints;
    }

    // One limb segment split into several bones: in line, and named as numbered parts of one bone
    // ("shin", "shin.001"). Being in line is not enough on its own: a leg straightened mid-stride
    // has its real joints in line too, and would lose its knee or hock.
    private static bool SameSegment(Transform upper, Transform lower, float angle) =>
        angle < ColinearAngle && BaseName(upper.name) == BaseName(lower.name);

    private static string BaseName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"[._ ]?\d+$", "");

    // The bones that skin some vertex. Rigify and similar rigs export their control and
    // mechanism bones in the bone list too, and those hang down to the ground like feet.
    private static IEnumerable<Transform> DeformingBones(SkinnedMeshRenderer renderer)
    {
        Transform[] all = renderer.bones;
        Mesh mesh = renderer.sharedMesh;
        if (mesh == null)
            return all;
        var used = new bool[all.Length];
        bool any = false;
        foreach (BoneWeight1 weight in mesh.GetAllBoneWeights())
            if (weight.weight > 0f && weight.boneIndex < used.Length)
                any = used[weight.boneIndex] = true;
        return any ? all.Where((_, i) => used[i]) : all;
    }

    // Names the legs front/hind and left/right from where the feet sit around the character.
    private static bool Classify(List<Leg> legs, Transform character, out string problem)
    {
        problem = null;
        Vector3 forward = Vector3.ProjectOnPlane(character.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float Side(Leg l) => Vector3.Dot(l.anchor.position - character.position, right);
        float Ahead(Leg l) => Vector3.Dot(l.anchor.position - character.position, forward);

        void Name(int index, string label, string suffix)
        {
            Leg l = legs[index];
            l.label = label;
            l.suffix = suffix;
            legs[index] = l;
        }

        if (legs.Count == 2)
        {
            legs.Sort((a, b) => Side(a).CompareTo(Side(b)));
            Name(0, "LeftFoot", "LF");
            Name(1, "RightFoot", "RF");
            return true;
        }
        if (legs.Count == 4)
        {
            legs.Sort((a, b) => Ahead(b).CompareTo(Ahead(a)));
            if (Side(legs[0]) > Side(legs[1])) (legs[0], legs[1]) = (legs[1], legs[0]);
            if (Side(legs[2]) > Side(legs[3])) (legs[2], legs[3]) = (legs[3], legs[2]);
            Name(0, "FrontLeftFoot", "FL");
            Name(1, "FrontRightFoot", "FR");
            Name(2, "HindLeftFoot", "HL");
            Name(3, "HindRightFoot", "HR");
            return true;
        }

        problem = $"Found {legs.Count} legs, but the setup handles two or four."
                  + (legs.Count > 0 ? " Feet found: " + string.Join(", ", legs.Select(l => l.tip.name)) + "." : "")
                  + " The character should be in its rest pose, standing on its feet.";
        return false;
    }

    private static MultiParentConstraint FootRef(Transform rig, Leg leg)
    {
        GameObject go = Child(rig, "FootRef" + leg.suffix);
        go.transform.SetPositionAndRotation(leg.tip.position, leg.tip.rotation);
        var constraint = go.AddComponent<MultiParentConstraint>();
        ref var data = ref constraint.data;
        data.constrainedObject = go.transform;
        var sources = new WeightedTransformArray(0);
        sources.Add(new WeightedTransform(leg.tip, 1f));
        data.sourceObjects = sources;
        data.constrainedPositionXAxis = data.constrainedPositionYAxis = data.constrainedPositionZAxis = true;
        data.constrainedRotationXAxis = data.constrainedRotationYAxis = data.constrainedRotationZAxis = true;
        data.maintainPositionOffset = data.maintainRotationOffset = false;
        return constraint;
    }

    private static TwoBoneIKConstraint FootIK(Transform rig, Leg leg, Transform character, out Transform target)
    {
        GameObject go = Child(rig, leg.label + "IK");
        target = Child(go.transform, leg.label + "IKTarget").transform;
        target.SetPositionAndRotation(leg.tip.position, leg.tip.rotation);
        var constraint = go.AddComponent<TwoBoneIKConstraint>();
        ref var data = ref constraint.data;
        data.root = leg.root;
        data.mid = leg.mid;
        data.tip = leg.tip;
        data.target = target;
        data.hint = Hint(leg, character);
        data.targetPositionWeight = data.targetRotationWeight = data.hintWeight = 1f;
        data.maintainTargetPositionOffset = data.maintainTargetRotationOffset = false;
        return constraint;
    }

    /// <summary>
    /// Where the middle joint points. Without a hint the solver takes the bend plane from the
    /// animated pose, and a leg animated almost straight (the deer's hocks bend ~4 degrees) flips
    /// that plane from frame to frame, spinning the lower leg. Parented to the leg's root bone so
    /// it swings with the animation.
    /// </summary>
    private static Transform Hint(Leg leg, Transform character)
    {
        string name = leg.label + "IKHint";
        // Anywhere in the character: a rerun may pick another root bone for the same leg.
        foreach (Transform old in character.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToList())
            Undo.DestroyObjectImmediate(old.gameObject);

        // Anatomy, not the default pose (which may be mid-stride or oddly posed): a hind leg's
        // hock points backwards, knees and front knees forwards.
        Vector3 forward = Vector3.ProjectOnPlane(character.forward, Vector3.up).normalized;
        Vector3 bend = leg.label.StartsWith("Hind") ? -forward : forward;
        Vector3 axis = leg.tip.position - leg.root.position;
        Vector3 across = Vector3.ProjectOnPlane(bend, axis.normalized);
        if (across.sqrMagnitude > 1e-4f)
            bend = across.normalized;

        float length = Vector3.Distance(leg.root.position, leg.mid.position) + Vector3.Distance(leg.mid.position, leg.tip.position);
        var hint = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(hint.gameObject, Title);
        hint.SetParent(leg.root, false);
        hint.position = leg.mid.position + bend * (length * 0.5f);
        return hint;
    }

    private static GameObject Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    // Every serialized setting of a foot, references to the rig itself excepted (those are rebuilt).
    private static readonly string[] FootSettings =
    {
        "rayYOffset", "rayDistance", "plantedYOffset", "ExitIKThreashhold", "ToIkSpeed", "ToNoIkSpeed",
        "IKFootRotationSpeed", "UpdateArmature", "mask", "playerLayer", "playerColliderUpdater", "CameraHeightAdjusterScript",
    };

    private static Dictionary<string, object> Snapshot(FootIKScript foot)
    {
        var so = new SerializedObject(foot);
        var values = new Dictionary<string, object>();
        foreach (string name in FootSettings)
        {
            SerializedProperty p = so.FindProperty(name);
            values[name] = p.propertyType switch
            {
                SerializedPropertyType.Float => p.floatValue,
                SerializedPropertyType.Boolean => p.boolValue,
                SerializedPropertyType.LayerMask => p.intValue,
                _ => (object)p.objectReferenceValue,
            };
        }
        var source = so.FindProperty("footRefConstraint").objectReferenceValue as MultiParentConstraint;
        values[OldTipKey] = source != null && source.data.sourceObjects.Count > 0 ? source.data.sourceObjects[0].transform : null;
        return values;
    }

    // Snapshot entry holding the bone the old foot followed; not a serialized setting.
    private const string OldTipKey = "__tip";

    /// <summary>
    /// How far the foot bone sits above the sole: its height over the lowest bone hanging below
    /// it (the hoof tip, a toe end). Measured within the leg, so a lifted mid-stride default pose
    /// does not matter. <paramref name="fallback"/> when nothing hangs below it.
    /// </summary>
    private static float PlantedHeight(Transform tip, float fallback)
    {
        float lowest = tip.position.y;
        foreach (Transform t in tip.GetComponentsInChildren<Transform>(true))
            lowest = Mathf.Min(lowest, t.position.y);
        float height = tip.position.y - lowest;
        return height > 1e-3f ? height : fallback;
    }

    private static void Restore(SerializedObject so, Dictionary<string, object> values)
    {
        foreach (var pair in values)
        {
            if (pair.Key == OldTipKey)
                continue;
            SerializedProperty p = so.FindProperty(pair.Key);
            switch (pair.Value)
            {
                case float f: p.floatValue = f; break;
                case bool b: p.boolValue = b; break;
                case int i: p.intValue = i; break;
                default: p.objectReferenceValue = pair.Value as Object; break;
            }
        }
    }

    // Model files whose clips key the armature object, with that object's path in them.
    private static readonly List<(string model, string path)> pendingMasks = new List<(string, string)>();

    /// <summary>
    /// Clips that key the armature object itself reset it every frame, so ArmatureIK's height
    /// and tilt never stick. Blender exports the armature object at its rest transform in every
    /// clip; those constant keys are queued to be masked out of the model's clips. Keys that
    /// actually move it are real animation and are only reported.
    /// </summary>
    private static void FindArmatureCurves(Animator animator, Transform armature, StringBuilder report)
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        if (controller == null)
            return;
        string path = AnimationUtility.CalculateTransformPath(armature, animator.transform);
        foreach (AnimationClip clip in controller.animationClips.Distinct())
        {
            var keyed = AnimationUtility.GetCurveBindings(clip).Where(b => b.path == path).ToList();
            if (keyed.Count == 0)
                continue;
            string asset = AssetDatabase.GetAssetPath(clip);
            if (keyed.Any(b => !IsConstant(AnimationUtility.GetEditorCurve(clip, b))))
                report.AppendLine($"  Warning: clip '{clip.name}' animates '{armature.name}', so the Animator overrides ArmatureIK on it.");
            else if (AssetImporter.GetAtPath(asset) is ModelImporter)
            {
                if (!pendingMasks.Contains((asset, path)))
                    pendingMasks.Add((asset, path));
            }
            else
                report.AppendLine($"  Warning: clip '{clip.name}' keys '{armature.name}'; remove those curves or ArmatureIK cannot move it.");
        }
    }

    private static bool IsConstant(AnimationCurve curve)
    {
        if (curve == null || curve.length == 0)
            return true;
        float first = curve.keys[0].value;
        return curve.keys.All(k => Mathf.Abs(k.value - first) < 1e-4f);
    }

    // Unticks the armature object in every clip's transform mask, leaving its bones animated.
    private static void MaskArmatureOutOfModels()
    {
        foreach (var (model, path) in pendingMasks)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(model);
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips.Length == 0)
                clips = importer.defaultClipAnimations;

            Transform root = AssetDatabase.LoadAssetAtPath<GameObject>(model).transform;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var mask = new AvatarMask { transformCount = all.Length };
            for (int i = 0; i < all.Length; i++)
            {
                string p = AnimationUtility.CalculateTransformPath(all[i], root);
                mask.SetTransformPath(i, p);
                mask.SetTransformActive(i, p != path);
            }

            var masked = new List<string>();
            foreach (ModelImporterClipAnimation clip in clips)
            {
                // A mask set up by hand is left alone: merging into it is not worth the guesswork.
                if (clip.maskType != ClipAnimationMaskType.None)
                {
                    Debug.LogWarning($"{Title}: clip '{clip.name}' in {model} already has a mask; untick '{path}' in it so ArmatureIK can move the armature.");
                    continue;
                }
                clip.maskType = ClipAnimationMaskType.CreateFromThisModel;
                clip.ConfigureClipFromMask(mask);
                masked.Add(clip.name);
            }
            Object.DestroyImmediate(mask);

            if (masked.Count == 0)
                continue;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            Debug.Log($"{Title}: masked '{path}' out of {string.Join(", ", masked)} in {model}; its keys only held the rest transform, and they stopped ArmatureIK moving and tilting it.");
        }
        pendingMasks.Clear();
    }

    private static bool Fail(string message)
    {
        EditorUtility.DisplayDialog(Title, message, "OK");
        return false;
    }
}
