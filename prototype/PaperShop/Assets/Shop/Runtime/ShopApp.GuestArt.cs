using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        readonly System.Random appearanceRandom=new System.Random();
        public const float EmployeeVisitChance=.025f;
        public int EmployeeVisits {get;private set;}
        public static bool IsEmployeeOffDuty(ShopEconomy state,string id)
        {
            if(!state.IsStaffUnlocked(id)||state.IsPerformingStaff(id))return false;
            foreach(var post in state.staffPosts)
                if(post.staffId==id||state.EffectiveStaffId(post)==id)return false;
            return true;
        }
        void ConfigureGuestAppearance(Guest guest)
        {
            var candidates=new List<string>();
            foreach(string id in new[]{"anon","tomori","soyo","taki","rana","uika","mutsumi","umiri","nyamu","sakiko"})
                if(IsEmployeeOffDuty(Economy,id)&&!guests.Exists(g=>g.visitingStaffId==id))candidates.Add(id);
            // Ordinary visitors use their own art; off-duty employee visits remain rare.
            string[] all={"guest01","guest02","guest03","guest04","guest05"};
            string skin=all[appearanceRandom.Next(all.Length)];
            if(!verifyMode&&candidates.Count>0&&appearanceRandom.NextDouble()<EmployeeVisitChance)
            {skin=candidates[appearanceRandom.Next(candidates.Count)];guest.visitingStaffId=skin;EmployeeVisits++;}
            guest.view.SetCharacter(skin);guest.view.SetPose("idle");
        }
        void UpdateGuestPose(Guest g)
        {
            g.view.SetPose(GuestCardPose(g.phase,g.route.Count>0,g.bought));
        }
        public static string GuestCardPose(GuestPhase phase,bool moving,int items)
        {
            // Shopping takes precedence over the basket: a customer who selected
            // something earlier still browses and waits at the next display empty-handed.
            switch(phase)
            {
                case GuestPhase.Paying:return "pay";
                case GuestPhase.ShoppingQueue:return moving?"walk":"think";
                case GuestPhase.Browsing:return "interact";
                case GuestPhase.WaitingGuide:return moving?"walk":"think";
                case GuestPhase.CheckoutQueue:return items>0?"carry":moving?"walk":"think";
                case GuestPhase.Leaving:return items>0?"carry":moving?"walk":"idle";
                default:return moving?"walk":"idle";
            }
        }
        bool IsVisitingEmployee(string id)=>!string.IsNullOrEmpty(id)&&guests.Exists(g=>g.visitingStaffId==id);
    }
}
