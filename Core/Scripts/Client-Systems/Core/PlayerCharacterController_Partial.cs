using MultiplayerARPG;

namespace MultiplayerARPG
{
    public partial class PlayerCharacterController
    {
        /// <summary>
        /// Clears selected client-side entity safely (Harvest-System use).
        /// Does NOT affect server target or combat state.
        /// </summary>
        public void ClearSelectedClientEntity()
        {
            SelectedEntity = null;
            TargetEntity = null;

            if (UISceneGameplay != null)
            {
                UISceneGameplay.SetTargetEntity(null);
            }
        }
    }
}
