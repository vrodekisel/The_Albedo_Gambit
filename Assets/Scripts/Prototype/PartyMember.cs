using UnityEngine;

namespace AlbedoGambit.Prototype
{
    public enum ChessRole { King, Rook, Bishop, Knight }

    [DisallowMultipleComponent]
    public sealed class PartyMember : MonoBehaviour
    {
        [SerializeField] private string characterId = "A";
        [SerializeField] private ChessRole futureRole;
        public string CharacterId => characterId;
        public ChessRole FutureRole => futureRole;

        public void Configure(string id, ChessRole role)
        {
            characterId = id;
            futureRole = role;
        }
    }
}
