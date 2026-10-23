using UnityEngine;

namespace AlbedoGambit.Prototype
{
    public sealed class BattleEnemyView : MonoBehaviour
    {
        public int Participant { get; private set; } = -1;
        public void Bind(int participant) { Participant = participant; }
    }
}
