using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RobotAgent : MonoBehaviour
{
    public string id = "robot1";
    public WorldBuilder builder;
    public float fovDeg = 120f;
    public float range = 10f;

    public bool hasExtinguisher = false;
    public string holdingId = null;    // generic pick/drop carry id
    public Transform target;           // move target (ghost or real)

    LineRenderer fovLine;
    const int FovSegments = 36;

    void Update()
    {
        // simple continuous move toward target (used by step ghost + safe zone)
        if (target != null)
        {
            var pos = transform.position;
            var tpos = new Vector3(target.position.x, pos.y, target.position.z);
            transform.position = Vector3.MoveTowards(pos, tpos, 4f * Time.deltaTime);
            Vector3 flat = (tpos - pos); flat.y = 0f;
            if (flat.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(flat);
            if (Vector3.Distance(transform.position, tpos) < 0.3f) target = null;
        }

        RevealInFov();
        DrawFovCone();
    }

    // ---------- movement ----------
    public void MoveDir(string dir, float stepMeters)
    {
        Vector3 d = Vector3.zero;
        switch (dir)
        {
            case "forward": d = transform.forward; break;
            case "back":    d = -transform.forward; break;
            case "left":    d = -transform.right; break;
            case "right":   d = transform.right; break;
        }
        Vector3 dest = builder != null ? builder.ClampStep(transform.position, d.normalized * stepMeters)
                                       : transform.position + d.normalized * stepMeters;

        var ghost = new GameObject($"{id}_step");
        ghost.transform.position = new Vector3(dest.x, transform.position.y, dest.z);
        target = ghost.transform;
        StartCoroutine(DestroyWhenClose(this, ghost.transform, 0.35f));
    }

    public void GoTo(string objId)
    {
        if (builder != null && builder.id2obj.TryGetValue(objId, out var tr))
            target = tr;
    }

    // ---------- FoV / observability ----------
    public bool IsInFov(Transform tr)
    {
        if (!tr) return false;
        Vector3 to = tr.position - transform.position; to.y = 0f;
        if (to.magnitude > range) return false;
        float ang = Vector3.Angle(transform.forward, to);
        return ang <= (fovDeg * 0.5f);
    }

    public void RevealInFov()
    {
        if (builder == null) return;
        foreach (var kv in builder.id2obj)
        {
            var tr = kv.Value; if (!tr) continue;
            if (kv.Key.StartsWith("Room") || kv.Key == "Floor" || kv.Key == id) { tr.gameObject.SetActive(true); continue; }
            if (IsInFov(tr)) tr.gameObject.SetActive(true);
        }
    }

    public void EnableFovCone(bool enable)
    {
        if (!enable)
        {
            if (fovLine) fovLine.gameObject.SetActive(false);
            return;
        }
        if (!fovLine)
        {
            var go = new GameObject($"{id}_FoV");
            go.transform.SetParent(transform, false);
            fovLine = go.AddComponent<LineRenderer>();
            fovLine.useWorldSpace = false;
            fovLine.widthMultiplier = 0.025f;
            fovLine.loop = true;
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = new Color(0.2f, 0.8f, 1f, 0.8f);
            fovLine.material = mat;
            fovLine.positionCount = FovSegments + 2;
        }
        fovLine.gameObject.SetActive(true);
    }

    void DrawFovCone()
    {
        if (fovLine == null || !fovLine.gameObject.activeSelf) return;
        float half = fovDeg * 0.5f;
        fovLine.positionCount = FovSegments + 2;
        fovLine.SetPosition(0, Vector3.zero);
        for (int i = 0; i <= FovSegments; i++)
        {
            float t = i / (float)FovSegments;
            float angle = -half + (t * fovDeg);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            fovLine.SetPosition(i + 1, dir * range);
        }
    }

    // ---------- actions ----------
    public void PickExtinguisher(string _) { hasExtinguisher = true; Debug.Log($"{id}: extinguisher equipped"); }

    // 🔥 destroy every Fire in FoV (one sphere = one fire)
    public void ExtinguishAllInFov()
    {
        if (!hasExtinguisher) { hasExtinguisher = true; Debug.Log($"{id}: auto-equipped extinguisher"); }

        var fires = GameObject.FindObjectsOfType<Fire>();
        int count = 0;
        foreach (var fire in fires)
        {
            if (!fire) continue;
            var tr = fire.transform;
            if (!tr.gameObject.activeInHierarchy) continue;
            if (!IsInFov(tr)) continue;

            Debug.Log($"{id}: extinguish -> {fire.id}");
            count++;
            GameObject.Destroy(tr.gameObject);   // disappear for sure
        }
        Debug.Log($"{id}: extinguished {count} fire(s) in FoV");

        if (builder != null) builder.PauseDynamicsBriefly(1.0f);
    }

    // --- generic pick/drop/deliver stubs used by WorldBuilder.Execute ---
    public void Pick(string targetId)
    {
        if (builder == null || string.IsNullOrEmpty(targetId)) return;
        if (!builder.id2obj.TryGetValue(targetId, out var tr)) return;

        if (Vector3.Distance(transform.position, tr.position) > 1.5f) { GoTo(targetId); return; }
        holdingId = targetId;
        tr.SetParent(transform);
        tr.localPosition = new Vector3(0, 1.0f, 0.6f);
    }

    public void Drop(string _)
    {
        if (builder == null || holdingId == null) return;
        var tr = builder.id2obj[holdingId];
        tr.SetParent(null);
        tr.position = transform.position + transform.forward * 0.6f;
        holdingId = null;
    }

    public void Deliver(string targetId)
    {
        if (builder == null || holdingId == null || string.IsNullOrEmpty(targetId)) return;
        if (!builder.id2obj.TryGetValue(targetId, out var t)) return;

        if (Vector3.Distance(transform.position, t.position) > 1.5f) { GoTo(targetId); return; }
        var carried = builder.id2obj[holdingId];
        carried.gameObject.SetActive(false); // delivered/consumed
        holdingId = null;
        Debug.Log($"{id}: delivered to {targetId}");
    }

    // --- rescue flow (robot2) ---
    public void RescueNearestInFov()
    {
        if (builder == null || builder.safeZone == null) { Debug.LogWarning($"{id}: SafeZone missing"); return; }

        // carrying? go to safe zone
        if (!string.IsNullOrEmpty(holdingId) && holdingId.StartsWith("Human"))
        {
            var ghost = new GameObject($"{id}_safezone");
            ghost.transform.position = builder.safeZone.position;
            target = ghost.transform;
            StartCoroutine(DestroyWhenClose(this, ghost.transform, 0.6f));
            // release will be handled after we arrive; for now just drop instantly:
            var tr = builder.id2obj[holdingId];
            tr.SetParent(null);
            tr.position = builder.safeZone.position + new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            tr.gameObject.SetActive(false);
            Debug.Log($"{id}: rescued {holdingId} to SafeZone");
            holdingId = null;
            return;
        }

        // find nearest Human in FoV
        string best = null; float bestD = float.MaxValue;
        foreach (var kv in builder.id2obj)
        {
            if (!kv.Key.StartsWith("Human")) continue;
            var tr = kv.Value; if (!tr || !tr.gameObject.activeSelf) continue;
            if (!IsInFov(tr)) continue;
            float d = Vector3.Distance(transform.position, tr.position);
            if (d < bestD) { bestD = d; best = kv.Key; }
        }
        if (best == null) { Debug.Log($"{id}: no survivor in FoV"); return; }

        // pick it
        Pick(best);
    }

    // ---------- utils ----------
    static IEnumerator DestroyWhenClose(RobotAgent agent, Transform ghost, float thresh)
    {
        while (agent != null && ghost != null)
        {
            if (Vector3.Distance(agent.transform.position, ghost.position) < thresh) break;
            yield return null;
        }
        if (ghost != null) GameObject.Destroy(ghost.gameObject);
    }
}
