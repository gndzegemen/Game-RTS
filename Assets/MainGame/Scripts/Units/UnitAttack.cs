using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// "Hedefe yürü, menzile girince bekleme süresiyle saldır" davranışının ortak hali (Template Method).
    /// Fighter ve Cutter neredeyse aynı kodun kopyasıydı; artık yalnızca hangi hedefe saldırabileceklerini
    /// ve menzil/hasar değerlerini tanımlarlar. Tüm mantık sunucuda çalışır.
    /// </summary>
    [RequireComponent(typeof(Mover))]
    public abstract class UnitAttack : NetworkBehaviour, IAction
    {
        private const string AttackTrigger = "attack";

        [SerializeField] protected float timeBetweenAttacks = 1f;

        private Mover mover;
        private ActionScheduler actionScheduler;
        private Animator animator;
        private NetworkAnimator networkAnimator;

        private Health target;
        private Collider[] targetColliders;
        private float timeSinceLastAttack = Mathf.Infinity;

        protected abstract float Range { get; }
        protected abstract float Damage { get; }

        /// <summary>Bu unit'in verilen objeye saldırıp saldıramayacağı. Hem client'ta (komut verirken) hem sunucuda (doğrulama) kullanılır.</summary>
        public abstract bool CanAttack(GameObject candidate);

        protected virtual void Awake()
        {
            mover = GetComponent<Mover>();
            actionScheduler = GetComponent<ActionScheduler>();
            animator = GetComponent<Animator>();
            networkAnimator = GetComponent<NetworkAnimator>();
        }

        /// <summary>Sunucuda çağrılır.</summary>
        public void StartAttack(NetworkObject targetObject)
        {
            if (!IsServer || targetObject == null)
            {
                return;
            }

            var targetHealth = targetObject.GetComponent<Health>();
            if (targetHealth == null || targetHealth.IsDead)
            {
                return;
            }

            if (actionScheduler != null)
            {
                actionScheduler.StartAction(this);
            }

            target = targetHealth;
            targetColliders = targetObject.GetComponentsInChildren<Collider>();
        }

        public void Cancel()
        {
            target = null;
            targetColliders = null;
        }

        private void Update()
        {
            if (!IsServer)
            {
                return;
            }

            timeSinceLastAttack += Time.deltaTime;

            if (target == null)
            {
                return;
            }

            if (!target.IsSpawned || target.IsDead)
            {
                Cancel();
                return;
            }

            if (!IsInRange())
            {
                mover.MoveTo(target.transform.position);
                return;
            }

            mover.Cancel();
            FaceTarget();

            if (timeSinceLastAttack >= timeBetweenAttacks)
            {
                TriggerAttackAnimation();
                timeSinceLastAttack = 0f;
            }
        }

        /// <summary>
        /// Saldırı animasyonundaki (Attack01.anim) animasyon event'i tarafından çağrılır.
        /// Animator her makinede çalıştığı için event her yerde tetiklenir; hasar yalnızca sunucuda uygulanır.
        /// </summary>
        private void Hit()
        {
            if (!IsServer || target == null || !IsInRange())
            {
                return;
            }

            target.TakeDamage(Damage, OwnerClientId);
        }

        /// <summary>
        /// Mesafe hedefin merkezine değil, collider sınırına göre ölçülür. Böylece büyük binalara
        /// (NavMeshObstacle yüzünden merkezine yaklaşılamayan) da saldırılabilir.
        /// </summary>
        private bool IsInRange()
        {
            Vector3 position = transform.position;
            float closestDistance = Vector3.Distance(position, target.transform.position);

            if (targetColliders != null)
            {
                foreach (var targetCollider in targetColliders)
                {
                    if (targetCollider == null)
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(position, targetCollider.bounds.ClosestPoint(position));
                    closestDistance = Mathf.Min(closestDistance, distance);
                }
            }

            return closestDistance <= Range;
        }

        private void FaceTarget()
        {
            Vector3 lookPosition = target.transform.position;
            lookPosition.y = transform.position.y;
            if ((lookPosition - transform.position).sqrMagnitude > 0.001f)
            {
                transform.LookAt(lookPosition);
            }
        }

        private void TriggerAttackAnimation()
        {
            if (networkAnimator != null)
            {
                networkAnimator.SetTrigger(AttackTrigger);
            }
            else if (animator != null)
            {
                animator.SetTrigger(AttackTrigger);
            }
        }
    }
}
