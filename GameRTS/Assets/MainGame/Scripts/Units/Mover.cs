using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MainGame.Scripts
{
    /// <summary>
    /// NavMeshAgent ile hareket. Ajan yalnızca sunucuda çalışır; pozisyon NetworkTransform,
    /// animasyon parametreleri NetworkAnimator ile client'lara aktarılır.
    /// (Eski Mover ve MoverWoodCutter birebir aynı kodun kopyasıydı, tek sınıfta birleştirildi.)
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class Mover : NetworkBehaviour, IAction
    {
        private static readonly int ForwardSpeedParameter = Animator.StringToHash("forwardSpeed");

        private NavMeshAgent agent;
        private Animator animator;
        private ActionScheduler actionScheduler;

        private bool CanControlAgent => IsServer && agent.enabled && agent.isOnNavMesh;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponent<Animator>();
            actionScheduler = GetComponent<ActionScheduler>();

            // Ajan spawn'dan önce açık kalırsa NetworkTransform'un ilk senkronizasyonu ajanı NavMesh üzerinde
            // başka bir noktaya ışınlıyordu (okçu/swat spawn noktasında ~15 m). Spawn tamamlanınca açılır.
            agent.enabled = false;
        }

        public override void OnNetworkSpawn()
        {
            // Client'larda ajan, NetworkTransform ile gelen pozisyonla çakışmasın diye kapalı kalır.
            if (!IsServer)
            {
                return;
            }

            agent.enabled = true;
            agent.Warp(transform.position);
        }

        private void Update()
        {
            if (IsServer)
            {
                UpdateAnimator();
            }
        }

        /// <summary>Yeni bir hareket aksiyonu başlatır (mevcut saldırı vb. iptal edilir). Sunucuda çağrılır.</summary>
        public void StartMoveAction(Vector3 destination)
        {
            if (actionScheduler != null)
            {
                actionScheduler.StartAction(this);
            }

            MoveTo(destination);
        }

        /// <summary>Aksiyonu değiştirmeden hedefe yürür (ör. saldırı menziline yaklaşırken). Sunucuda çağrılır.</summary>
        public void MoveTo(Vector3 destination)
        {
            if (!CanControlAgent)
            {
                return;
            }

            agent.destination = destination;
            agent.isStopped = false;
        }

        public void Cancel()
        {
            if (CanControlAgent)
            {
                agent.isStopped = true;
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null)
            {
                return;
            }

            Vector3 localVelocity = transform.InverseTransformDirection(agent.velocity);
            animator.SetFloat(ForwardSpeedParameter, localVelocity.z);
        }
    }
}
