using UnityEngine;

namespace MainGame.Scripts
{
    /// <summary>
    /// Bir unit'in aynı anda yalnızca tek bir aksiyonu olmasını sağlar.
    /// Yeni aksiyon başlarken öncekini iptal eder.
    /// </summary>
    public class ActionScheduler : MonoBehaviour
    {
        private IAction currentAction;

        public void StartAction(IAction action)
        {
            if (currentAction == action)
            {
                return;
            }

            currentAction?.Cancel();
            currentAction = action;
        }

        public void CancelCurrentAction()
        {
            StartAction(null);
        }
    }
}
