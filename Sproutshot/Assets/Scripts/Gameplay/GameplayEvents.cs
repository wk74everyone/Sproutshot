using Sproutshot.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public readonly struct OnCardSelectedEvent : IGameEvent
    {
        public readonly int SelectedCardId;

        public OnCardSelectedEvent(int selectedCardId)
        {
            SelectedCardId = selectedCardId;
        }
    }

    public class GameplayEvents
    {

    }
}