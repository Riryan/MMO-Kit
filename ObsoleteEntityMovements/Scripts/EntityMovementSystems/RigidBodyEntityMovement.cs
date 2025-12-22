using LiteNetLib.Utils;
using LiteNetLibManager;
using StandardAssets.Characters.Physics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MultiplayerARPG
{
    [System.Obsolete("Should use `CharacterControllerEntityMovement` instead, can convert by \"Convert To Character Controller Entity Movement\" context menu.")]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(OpenCharacterController))]
    public class RigidBodyEntityMovement : BaseNetworkedGameEntityComponent<BaseGameEntity>, IEntityMovementComponent, IBuiltInEntityMovement3D
    {
        [Header("Movement AI")]
        [Range(0.01f, 1f)]
        public float stoppingDistance = 0.1f;
        public MovementSecure movementSecure = MovementSecure.NotSecure;

        [Header("Movement Settings")]
        public float jumpHeight = 2f;
        public ApplyJumpForceMode applyJumpForceMode = ApplyJumpForceMode.ApplyImmediately;
        public float applyJumpForceFixedDuration;
        public float backwardMoveSpeedRate = 0.75f;
        public float gravity = 9.81f;
        public float maxFallVelocity = 40f;
        [Tooltip("Delay before character change from grounded state to airborne")]
        public float airborneDelay = 0.01f;
        public bool doNotChangeVelocityWhileAirborne;
        public float landedPauseMovementDuration = 0f;
        public float beforeCrawlingPauseMovementDuration = 0f;
        public float afterCrawlingPauseMovementDuration = 0f;
        [Range(0.1f, 1f)]
        public float underWaterThreshold = 0.75f;
        public bool autoSwimToSurface;

        [Header("Ground checking")]
        public float groundCheckYOffsets = 0.1f;
        public float forceUngroundAfterJumpDuration = 0.1f;
        public Color groundCheckGizmosColor = Color.blue;

        [Header("Root Motion Settings")]
        [FormerlySerializedAs("useRootMotionWhileNotMoving")]
        public bool alwaysUseRootMotion;
        public bool useRootMotionForMovement;
        public bool useRootMotionForAirMovement;
        public bool useRootMotionForJump;
        public bool useRootMotionForFall;
        public bool useRootMotionUnderWater;

        [Header("Networking Settings")]
        public float snapThreshold = 5.0f;

        protected Animator _cacheAnimator;
        public Animator CacheAnimator
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && _cacheAnimator == null)
                    _cacheAnimator = GetComponent<Animator>();
#endif
                return _cacheAnimator;
            }
            private set => _cacheAnimator = value;
        }
        protected Rigidbody _cacheRigidbody;
        public Rigidbody CacheRigidbody
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && _cacheRigidbody == null)
                    _cacheRigidbody = GetComponent<Rigidbody>();
#endif
                return _cacheRigidbody;
            }
            private set => _cacheRigidbody = value;
        }
        protected CapsuleCollider _cacheCapsuleCollider;
        public CapsuleCollider CacheCapsuleCollider
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && _cacheCapsuleCollider == null)
                    _cacheCapsuleCollider = GetComponent<CapsuleCollider>();
#endif
                return _cacheCapsuleCollider;
            }
            private set => _cacheCapsuleCollider = value;
        }
        protected OpenCharacterController _cacheOpenCharacterController;
        public OpenCharacterController CacheOpenCharacterController
        {
            get
            {
#if UNITY_EDITOR
                if (!Application.isPlaying && _cacheOpenCharacterController == null)
                    _cacheOpenCharacterController = GetComponent<OpenCharacterController>();
#endif
                return _cacheOpenCharacterController;
            }
            private set => _cacheOpenCharacterController = value;
        }
        public BuiltInEntityMovementFunctions3D Functions { get; private set; }

        public float StoppingDistance { get { return Functions.StoppingDistance; } }
        public MovementState MovementState { get { return Functions.MovementState; } }
        public ExtraMovementState ExtraMovementState { get { return Functions.ExtraMovementState; } }
        public DirectionVector2 Direction2D { get { return Functions.Direction2D; } set { Functions.Direction2D = value; } }
        public float CurrentMoveSpeed { get { return Functions.CurrentMoveSpeed; } }
        public Queue<Vector3> NavPaths { get { return Functions.NavPaths; } }
        public bool HasNavPaths { get { return Functions.HasNavPaths; } }

        protected float _forceUngroundCountdown = 0f;

        public override void EntityAwake()
        {
            // Prepare animator component
            CacheAnimator = GetComponent<Animator>();
            // Prepare rigidbody component
            CacheRigidbody = gameObject.GetOrAddComponent<Rigidbody>();
            // Prepare collider component
            CacheCapsuleCollider = gameObject.GetOrAddComponent<CapsuleCollider>();
            // Prepare open character controller
            float radius = CacheCapsuleCollider.radius;
            float height = CacheCapsuleCollider.height;
            Vector3 center = CacheCapsuleCollider.center;
            CacheOpenCharacterController = gameObject.GetOrAddComponent<OpenCharacterController>((comp) =>
            {
                comp.SetRadiusHeightAndCenter(radius, height, center, true, true);
            });
            CacheOpenCharacterController.collision += OnCharacterControllerCollision;
            // Disable unused component
            LiteNetLibTransform disablingComp = gameObject.GetComponent<LiteNetLibTransform>();
            if (disablingComp != null)
            {
                Logging.LogWarning(nameof(RigidBodyEntityMovement), "You can remove `LiteNetLibTransform` component from game entity, it's not being used anymore [" + name + "]");
                disablingComp.enabled = false;
            }
            // Setup
            Functions = new BuiltInEntityMovementFunctions3D(Entity, CacheAnimator, this)
            {
                stoppingDistance = stoppingDistance,
                movementSecure = movementSecure,
                jumpHeight = jumpHeight,
                applyJumpForceMode = applyJumpForceMode,
                applyJumpForceFixedDuration = applyJumpForceFixedDuration,
                backwardMoveSpeedRate = backwardMoveSpeedRate,
                gravity = gravity,
                maxFallVelocity = maxFallVelocity,
                stickGroundForce = 0f,
                airborneDelay = airborneDelay,
                doNotChangeVelocityWhileAirborne = doNotChangeVelocityWhileAirborne,
                landedPauseMovementDuration = landedPauseMovementDuration,
                beforeCrawlingPauseMovementDuration = beforeCrawlingPauseMovementDuration,
                afterCrawlingPauseMovementDuration = afterCrawlingPauseMovementDuration,
                underWaterThreshold = underWaterThreshold,
                autoSwimToSurface = autoSwimToSurface,
                alwaysUseRootMotion = alwaysUseRootMotion,
                useRootMotionForMovement = useRootMotionForMovement,
                useRootMotionForAirMovement = useRootMotionForAirMovement,
                useRootMotionForJump = useRootMotionForJump,
                useRootMotionForFall = useRootMotionForFall,
                useRootMotionUnderWater = useRootMotionUnderWater,
                snapThreshold = snapThreshold,
            };
            Functions.StopMoveFunction();
        }

        public override void EntityStart()
        {
            Functions.EntityStart();
            CacheOpenCharacterController.SetPosition(CacheTransform.position, true);
        }

        public override void ComponentOnEnable()
        {
            Functions.ComponentEnabled();
            CacheOpenCharacterController.enabled = true;
            CacheOpenCharacterController.SetPosition(CacheTransform.position, true);
        }

        public override void ComponentOnDisable()
        {
            CacheOpenCharacterController.enabled = false;
        }

        public override void EntityOnDestroy()
        {
            base.EntityOnDestroy();
            CacheOpenCharacterController.collision -= OnCharacterControllerCollision;
        }

        public override void OnSetOwnerClient(bool isOwnerClient)
        {
            base.OnSetOwnerClient(isOwnerClient);
            Functions.OnSetOwnerClient(isOwnerClient);
        }

        private void OnAnimatorMove()
        {
            Functions.OnAnimatorMove();
        }

        private void OnTriggerEnter(Collider other)
        {
            Functions.OnTriggerEnter(other);
        }

        private void OnTriggerExit(Collider other)
        {
            Functions.OnTriggerExit(other);
        }

        private void OnCharacterControllerCollision(OpenCharacterController.CollisionInfo hit)
        {
            Functions.OnControllerColliderHit(hit.point, hit.transform);
        }

        public override void EntityUpdate()
        {
            float deltaTime = Time.deltaTime;
            Functions.UpdateMovement(deltaTime);
            Functions.AfterMovementUpdate(deltaTime);
            if (_forceUngroundCountdown > 0f)
                _forceUngroundCountdown -= deltaTime;
        }

        public override void EntityLateUpdate()
        {
            float deltaTime = Time.deltaTime;
            Functions.UpdateRotation(deltaTime);
            Functions.FixSwimUpPosition(deltaTime);
        }

        public bool GroundCheck()
        {
            if (_forceUngroundCountdown > 0f)
                return false;
            if (CacheOpenCharacterController.isGrounded)
                return true;
            float radius = GetGroundCheckRadius();
            return Physics.CheckSphere(GetGroundCheckCenter(radius), radius, GameInstance.Singleton.GetGameEntityGroundDetectionLayerMask(), QueryTriggerInteraction.Ignore);
        }

        private Vector3 GetGroundCheckCenter(float radius)
        {
            return new Vector3(CacheTransform.position.x, CacheTransform.position.y + radius - groundCheckYOffsets, CacheTransform.position.z);
        }

        private float GetGroundCheckRadius()
        {
            return CacheOpenCharacterController.scaledRadius;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Color prevColor = Gizmos.color;
            Gizmos.color = groundCheckGizmosColor;
            float radius = GetGroundCheckRadius();
            Gizmos.DrawWireSphere(GetGroundCheckCenter(radius), radius);
            Gizmos.color = prevColor;
        }
#endif
        public Bounds GetBounds()
        {
            return CacheCapsuleCollider.bounds;
        }

        public void Move(Vector3 motion)
        {
            CacheOpenCharacterController.Move(motion);
        }

        public void RotateY(float yAngle)
        {
            CacheTransform.eulerAngles = new Vector3(0f, yAngle, 0f);
        }

        public void OnJumpForceApplied(float verticalVelocity)
        {
            _forceUngroundCountdown = forceUngroundAfterJumpDuration;
        }

        public bool WriteClientState(long writeTimestamp, NetDataWriter writer, out bool shouldSendReliably)
        {
            return Functions.WriteClientState(writeTimestamp, writer, out shouldSendReliably);
        }

        public bool WriteServerState(long writeTimestamp, NetDataWriter writer, out bool shouldSendReliably)
        {
            return Functions.WriteServerState(writeTimestamp, writer, out shouldSendReliably);
        }

        public void ReadClientStateAtServer(long peerTimestamp, NetDataReader reader)
        {
            Functions.ReadClientStateAtServer(peerTimestamp, reader);
        }

        public void ReadServerStateAtClient(long peerTimestamp, NetDataReader reader)
        {
            Functions.ReadServerStateAtClient(peerTimestamp, reader);
        }

        public void StopMove()
        {
            Functions.StopMove();
        }

        public void KeyMovement(Vector3 moveDirection, MovementState movementState)
        {
            Functions.KeyMovement(moveDirection, movementState);
        }

        public void PointClickMovement(Vector3 position)
        {
            Functions.PointClickMovement(position);
        }

        public void SetExtraMovementState(ExtraMovementState extraMovementState)
        {
            Functions.SetExtraMovementState(extraMovementState);
        }

        public void SetLookRotation(Quaternion rotation)
        {
            Functions.SetLookRotation(rotation);
        }

        public Quaternion GetLookRotation()
        {
            return Functions.GetLookRotation();
        }

        public void SetSmoothTurnSpeed(float speed)
        {
            Functions.SetSmoothTurnSpeed(speed);
        }

        public float GetSmoothTurnSpeed()
        {
            return Functions.GetSmoothTurnSpeed();
        }

        public void Teleport(Vector3 position, Quaternion rotation, bool stillMoveAfterTeleport)
        {
            Functions.Teleport(position, rotation, stillMoveAfterTeleport);
        }

        public bool FindGroundedPosition(Vector3 fromPosition, float findDistance, out Vector3 result)
        {
            return Functions.FindGroundedPosition(fromPosition, findDistance, out result);
        }

#if UNITY_EDITOR

        [ContextMenu("Convert To Character Controller Entity Movement")]
        public void ConvertToCharacterControllerEntityMovement()
        {
            try
            {
                CapsuleCollider collider = gameObject.GetComponent<CapsuleCollider>();
                if (collider != null)
                {
                    CharacterController characterController = gameObject.GetOrAddComponent<CharacterController>();
                    characterController.material = collider.material;
                    characterController.center = collider.center;
                    characterController.radius = collider.radius;
                    characterController.height = collider.height;
                }

                CharacterControllerEntityMovement entityMovement = gameObject.GetOrAddComponent<CharacterControllerEntityMovement>();
                entityMovement.stoppingDistance = stoppingDistance;
                entityMovement.movementSecure = movementSecure;

                entityMovement.jumpHeight = jumpHeight;
                entityMovement.applyJumpForceMode = applyJumpForceMode;
                entityMovement.applyJumpForceFixedDuration = applyJumpForceFixedDuration;
                entityMovement.backwardMoveSpeedRate = backwardMoveSpeedRate;
                entityMovement.gravity = gravity;
                entityMovement.maxFallVelocity = maxFallVelocity;

                entityMovement.airborneDelay = airborneDelay;
                entityMovement.doNotChangeVelocityWhileAirborne = doNotChangeVelocityWhileAirborne;
                entityMovement.landedPauseMovementDuration = landedPauseMovementDuration;
                entityMovement.beforeCrawlingPauseMovementDuration = beforeCrawlingPauseMovementDuration;
                entityMovement.afterCrawlingPauseMovementDuration = afterCrawlingPauseMovementDuration;

                entityMovement.underWaterThreshold = underWaterThreshold;
                entityMovement.autoSwimToSurface = autoSwimToSurface;

                entityMovement.useRootMotionForMovement = useRootMotionForMovement;
                entityMovement.useRootMotionForAirMovement = useRootMotionForAirMovement;
                entityMovement.useRootMotionForJump = useRootMotionForJump;
                entityMovement.useRootMotionForFall = useRootMotionForFall;
                entityMovement.alwaysUseRootMotion = alwaysUseRootMotion;
                entityMovement.useRootMotionUnderWater = useRootMotionUnderWater;

                entityMovement.snapThreshold = snapThreshold;
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.DisplayDialog("Entity Movement Conversion", "New Entity Movement component has been added.\n\nThe old component doesn't removed yet to let you check values.\n\nThen, you have to remove the old components (RigidBodyEntityMovement, OpenCharacterController, CapsuleCollider).", "OK");
            }
        }

        [ContextMenu("Applies Collider Settings To Controller")]
        public void AppliesColliderSettingsToController()
        {
            CapsuleCollider collider = gameObject.GetOrAddComponent<CapsuleCollider>();
            float radius = collider.radius;
            float height = collider.height;
            Vector3 center = collider.center;
            // Prepare open character controller
            OpenCharacterController controller = gameObject.GetOrAddComponent<OpenCharacterController>();
            controller.SetRadiusHeightAndCenter(radius, height, center, true, true);
        }
#endif
    }
}
