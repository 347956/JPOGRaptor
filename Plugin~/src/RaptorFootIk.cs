using UnityEngine;

namespace JPOGRaptor
{
    internal sealed class RaptorFootIk
    {
        private readonly Transform root;
        private readonly Leg left;
        private readonly Leg right;
        private readonly int groundMask;
        private readonly RaycastHit[] hits = new RaycastHit[12];

        private RaptorFootIk(Transform root, Leg left, Leg right)
        {
            this.root = root;
            this.left = left;
            this.right = right;
            groundMask = LayerMask.GetMask("Default", "Room", "Colliders", "NavigationSurface", "MiscLevelGeometry", "Terrain");
        }

        public static RaptorFootIk? Create(Animator animator)
        {
            Transform? hips = animator.transform.Find("Raptor/Scene_Root/D");
            if (hips == null)
                return null;

            // The rig has no toe joints. These foot-local points come from the sole of the skinned mesh.
            Leg? left = Leg.Find(hips, "L", new Vector3(0.00092f, 0.00078f, 0.00393f));
            Leg? right = Leg.Find(hips, "R", new Vector3(-0.00114f, 0.00073f, 0.00414f));
            return left != null && right != null ? new RaptorFootIk(animator.transform, left, right) : null;
        }

        public void RestoreAnimatedPose()
        {
            left.RestoreAnimatedPose();
            right.RestoreAnimatedPose();
        }

        public void Reset()
        {
            left.Reset();
            right.Reset();
        }

        public void Apply(float phase, float speed, float turn, bool allowPlanting, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            float scale = Mathf.Abs(root.lossyScale.y);
            RaycastHit floor = default;
            bool supported = allowPlanting && TryGround(root.position, scale, out floor);
            // The slow-walk clip still moves at zero speed. Avoid locking that motion to the floor laterally.
            float horizontalLock = Mathf.Lerp(1f, 0.2f, Mathf.Abs(turn)) * Mathf.InverseLerp(0.2f, 0.9f, speed);
            left.Apply(this, supported ? ContactWeight(phase, speed, false) : 0f, floor.point.y, horizontalLock, scale, deltaTime);
            right.Apply(this, supported ? ContactWeight(phase, speed, true) : 0f, floor.point.y, horizontalLock, scale, deltaTime);
        }

        private bool TryGround(Vector3 position, float scale, out RaycastHit ground)
        {
            position.y = root.position.y + 0.65f * scale;
            int count = Physics.RaycastNonAlloc(position, Vector3.down, hits, 1.5f * scale, groundMask,
                QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            ground = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.distance < nearest && hit.normal.y >= 0.64f && !hit.collider.transform.IsChildOf(root))
                {
                    nearest = hit.distance;
                    ground = hit;
                }
            }

            return !float.IsPositiveInfinity(nearest);
        }

        private static float ContactWeight(float phase, float speed, bool rightFoot)
        {
            // Walk plants just before the wrap and half a cycle later. The run plants later and releases earlier.
            float run = Mathf.InverseLerp(2f, 7f, speed);
            float contact = rightFoot ? Mathf.Lerp(0.43f, 0.51f, run) : Mathf.Lerp(0.93f, 0.98f, run);
            float sincePlant = Mathf.Repeat(phase - contact, 1f);
            float liftEnd = Mathf.Lerp(0.48f, 0.34f, run);
            float plant = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.085f, sincePlant));
            float lift = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(liftEnd - 0.11f, liftEnd, sincePlant));
            return Mathf.Lerp(1f, plant * lift, Mathf.InverseLerp(0.2f, 1.3f, speed));
        }

        private sealed class Leg
        {
            private readonly Transform thigh;
            private readonly Transform calf;
            private readonly Transform foot;
            private readonly Vector3 solePoint;
            private Quaternion animatedThigh;
            private Quaternion animatedCalf;
            private Quaternion animatedFoot;
            private bool poseApplied;
            private Vector3 anchor;
            private bool planted;
            private float weight;
            private Vector3 offset;
            private Quaternion tilt = Quaternion.identity;

            private Leg(Transform thigh, Transform calf, Transform foot, Vector3 solePoint)
            {
                this.thigh = thigh;
                this.calf = calf;
                this.foot = foot;
                this.solePoint = solePoint;
            }

            public static Leg? Find(Transform hips, string side, Vector3 solePoint)
            {
                Transform? thigh = hips.Find($"D_Thigh.{side}");
                Transform? calf = thigh?.Find($"D_Calf.{side}");
                Transform? foot = calf?.Find($"D_HorseLink.{side}/D_Foot.{side}");
                return thigh != null && calf != null && foot != null ? new Leg(thigh, calf, foot, solePoint) : null;
            }

            public void RestoreAnimatedPose()
            {
                if (!poseApplied)
                    return;

                thigh.localRotation = animatedThigh;
                calf.localRotation = animatedCalf;
                foot.localRotation = animatedFoot;
                poseApplied = false;
            }

            public void Reset()
            {
                RestoreAnimatedPose();
                planted = false;
                weight = 0f;
                offset = Vector3.zero;
                tilt = Quaternion.identity;
            }

            public void Apply(RaptorFootIk rig, float contact, float floorHeight, float horizontalLock, float scale, float deltaTime)
            {
                Vector3 sole = foot.TransformPoint(solePoint);
                Vector3 horizontal = Vector3.ProjectOnPlane(anchor - sole, Vector3.up);
                float maxLock = 0.4f * scale;
                if (contact < 0.2f || horizontalLock <= 0f || horizontal.sqrMagnitude > 4f * maxLock * maxLock)
                    planted = false;

                if (!planted && horizontalLock > 0f && contact >= 0.65f)
                {
                    planted = true;
                    anchor = sole;
                    horizontal = Vector3.zero;
                }

                horizontal = planted ? Vector3.ClampMagnitude(horizontal, maxLock) * horizontalLock : Vector3.zero;
                Vector3 targetOffset = Vector3.zero;
                Quaternion targetTilt = Quaternion.identity;
                if (contact > 0f && rig.TryGround(sole + horizontal, scale, out RaycastHit ground))
                {
                    targetOffset = horizontal;
                    // Apply the terrain difference while keeping the authored clearance above the floor.
                    targetOffset.y = Mathf.Clamp(ground.point.y - floorHeight, -0.35f * scale, 0.45f * scale);
                    targetTilt = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(Vector3.up, ground.normal), 24f);
                }
                else
                {
                    planted = false;
                    contact = 0f;
                }

                weight = Mathf.MoveTowards(weight, contact, deltaTime / 0.08f);
                float response = 1f - Mathf.Exp(-24f * deltaTime);
                offset = Vector3.Lerp(offset, targetOffset, response);
                tilt = Quaternion.Slerp(tilt, targetTilt, response);
                if (weight <= 0.001f)
                    return;

                Quaternion slope = Quaternion.Slerp(Quaternion.identity, tilt, weight);
                if (offset.sqrMagnitude < 0.000001f && slope.w > 0.9999999f)
                    return;

                Vector3 target = sole + offset * weight - slope * (sole - foot.position);
                Solve(target, slope * foot.rotation);
            }

            private void Solve(Vector3 target, Quaternion footRotation)
            {
                Vector3 hip = thigh.position;
                Vector3 upper = calf.position - hip;
                float upperLength = upper.magnitude;
                float lowerLength = Vector3.Distance(calf.position, foot.position);
                Vector3 toTarget = target - hip;
                Vector3 direction = toTarget.normalized;
                Vector3 bend = Vector3.ProjectOnPlane(upper, direction).normalized;
                if (upperLength < 0.01f || lowerLength < 0.01f || direction == Vector3.zero || bend == Vector3.zero)
                    return;

                float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.005f,
                    upperLength + lowerLength - 0.005f);
                float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
                float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
                Vector3 knee = hip + direction * along + bend * height;
                Vector3 reachableFoot = hip + direction * distance;

                animatedThigh = thigh.localRotation;
                animatedCalf = calf.localRotation;
                animatedFoot = foot.localRotation;
                poseApplied = true;

                thigh.rotation = Quaternion.FromToRotation(upper, knee - hip) * thigh.rotation;
                calf.rotation = Quaternion.FromToRotation(foot.position - calf.position, reachableFoot - calf.position) * calf.rotation;
                foot.rotation = footRotation;
            }
        }
    }
}
