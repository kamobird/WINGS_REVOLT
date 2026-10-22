using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Wings
{
    public sealed partial class WingsGame
    {
        [FormerlySerializedAs("Order"), SerializeField]
        SquadOrder squadOrder = SquadOrder.Defend;
        readonly Dictionary<int, SquadOrder> individualOrders = new Dictionary<int, SquadOrder>();
        public int CommandRecipient { get; set; } = -1;

        /// <summary>全員への指示。設定すると個別指示を解除するので、既存の全体命令と起動処理の意味を保てる。</summary>
        public SquadOrder Order
        {
            get => squadOrder;
            set
            {
                squadOrder = value;
                individualOrders.Clear();
            }
        }

        public SquadOrder OrderFor(PlayerAgent pilot)
        {
            return pilot && individualOrders.TryGetValue(pilot.Identity.Id, out var order) ? order : squadOrder;
        }

        /// <summary>recipient=-1は全員、0〜3は編成番号。操作中のキャラの指示も保持し、AIに戻ったときに利用する。</summary>
        public void IssueOrder(int recipient, SquadOrder order)
        {
            if (recipient == -1)
                Order = order;
            else if (recipient >= 0 && recipient < Party.Count && Party[recipient])
                individualOrders[Party[recipient].Identity.Id] = order;
        }

        bool CommandMatches(SquadOrder order)
        {
            if (CommandRecipient >= 0 && CommandRecipient < Party.Count)
                return OrderFor(Party[CommandRecipient]) == order;
            foreach (var pilot in Party)
                if (OrderFor(pilot) != order)
                    return false;
            return true;
        }
    }
}
