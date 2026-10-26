using System;
using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        readonly ShopResourceFeedback resourceFeedback = new ShopResourceFeedback();
        static bool IsRetiredPlaceholder(string id) => id=="fitting" || id=="decor";
        void RemoveRetiredPlaceholders()
        {
            if(Layout.facilities.RemoveAll(f=>IsRetiredPlaceholder(f.definitionId))>0)dirty=true;
        }
        void DrawDecorationStore(Rect box)
        {
            DrawStoreTabs(box);
            int row=0;
            foreach(string id in ShopEnvironmentAssets.Decorations)
            {
                float y=box.y+130+row++*102;int price=ShopEnvironmentAssets.Price(id);
                var icon=Resources.Load<Texture2D>("EnvironmentIcons/"+id);
                if(icon!=null)GUI.DrawTexture(new Rect(box.x+22,y,72,72),icon,ScaleMode.ScaleToFit);
                GUI.Label(new Rect(box.x+108,y+2,270,30),catalog.Find(id).title,heading);
                GUI.Label(new Rect(box.x+108,y+38,270,28),price+" 金币 · 库存 "+Economy.StoredFacilityCount(Layout,id),small);
                GUI.enabled=Economy.DecorationOwned(id)<99&&(Economy.testMode||Economy.cash>=price);
                if(ArtButton(new Rect(box.x+390,y+14,204,42),"购买 · "+price,activeButton))BuyEnvironmentItem(id);
                GUI.enabled=true;
            }
        }
        void DrawShopUpgrade(Rect box)
        {
            bool max=Economy.ExpansionLevel>=ShopEconomy.MaxAreaExpansionLevel;
            int target=Math.Max(Economy.ShopLevel,Economy.ExpansionLevel+2);
            GUI.Label(new Rect(box.x+22,box.y+60,570,34),max?"店铺 Lv.5 · 已满级":
                "店铺 Lv."+Economy.ShopLevel+" → Lv."+target,heading);
            if(!max)
            {
                bool pop=Economy.ExpansionQualifiedPopularity>=Economy.NextExpansionPopularity;
                bool coins=Economy.testMode||Economy.cash>=Economy.NextExpansionCost;
                GUI.Label(new Rect(box.x+22,box.y+108,570,27),"人气 "+Economy.ExpansionQualifiedPopularity+" / "+Economy.NextExpansionPopularity+
                    (pop?"  ✓ 已满足":"  · 还需 "+(Economy.NextExpansionPopularity-Economy.ExpansionQualifiedPopularity)),body);
                GUI.Label(new Rect(box.x+22,box.y+142,570,27),Economy.testMode?"金币 ∞ · 无限模式免扣款":
                    "金币 "+Economy.cash+" / "+Economy.NextExpansionCost+(coins?"  ✓ 已满足":"  · 还需 "+(Economy.NextExpansionCost-Economy.cash)),body);
                GUI.Label(new Rect(box.x+22,box.y+184,570,50),"升级后：面积 "+Economy.NextAreaWidth+" × "+Economy.NextAreaDepth+
                    " 格 · 人气上限 "+Economy.PopularityCap+"\n展柜与员工设施按对应等级解锁",body);
                GUI.enabled=pop&&coins&&CanRenovate;
                if(ArtButton(new Rect(box.x+22,box.y+246,570,40),CanRenovate?
                    (target==Economy.ShopLevel?"补齐店铺面积 · ":"升级店铺 · ")+(Economy.testMode?"免费":Economy.NextExpansionCost+" 金币"):
                    "请先关店，等待客人离开与演出结束",activeButton))ExpandShop();
                GUI.enabled=true;
            }
            else GUI.Label(new Rect(box.x+22,box.y+116,570,38),"12 × 12 格 · 人气上限 50 · 每类展柜上限 4",body);
            GUI.Label(new Rect(box.x+22,box.y+310,570,26),"当前岗位增益",heading);
            int row=0;
            foreach(var post in Economy.staffPosts)
                GUI.Label(new Rect(box.x+22,box.y+345+row++*29,570,27),
                    (post.role==FacilityKind.Checkout?"收银：":post.role==FacilityKind.Restock?"补货：":"招待：")+PostEffect(post),body);
        }
        void ResetResourceFeedback()
        {
            if(Economy!=null)resourceFeedback.Reset(Economy.cash,Economy.DecorationValue(Layout),0,Economy.popularity,Economy.testMode);
        }
        void TickResourceFeedback()
        {
            if(Economy==null)return;
            if(frontEnd||loadingScene){ResetResourceFeedback();return;}
            resourceFeedback.Observe(Time.unscaledTime,Economy.cash,Economy.DecorationValue(Layout),0,Economy.popularity,Economy.testMode);
        }
        void DrawResourceIcon(int index,Rect rect,Texture2D icon)
        {
            if(icon==null)return;
            float t=Time.unscaledTime-resourceFeedback.started[index];
            float scale=t<0||t>.24f?1:t<.08f?Mathf.Lerp(1,1.12f,t/.08f):t<.16f?Mathf.Lerp(1.12f,.98f,(t-.08f)/.08f):Mathf.Lerp(.98f,1,(t-.16f)/.08f);
            Vector2 center=rect.center;rect.size*=scale;rect.center=center;
            GUI.DrawTexture(rect,icon,ScaleMode.ScaleToFit);
        }
        void DrawResourceFeedback(Rect resources)
        {
            // Decorative, non-interactive HUD layer. Real balances have already changed.
            Color saved=GUI.color;
            var label=new GUIStyle(small){fontSize=14,alignment=TextAnchor.MiddleLeft};
            for(int i=0;i<3;i++)
            {
                float t=Time.unscaledTime-resourceFeedback.started[i];
                if(t<0||t>.7f)continue;
                float x=resources.x+12+i*resources.width/3;
                float alpha=Mathf.Clamp01(t/.08f)*Mathf.Clamp01((.7f-t)/.28f);
                long delta=resourceFeedback.displayed[i],cap=resourceFeedback.displayedCapacity;
                Action<string,long,int> text=(prefix,value,lineIndex)=>
                {
                    if(value==0)return;
                    label.normal.textColor=value>0?new Color(.32f,.39f,.27f,alpha):new Color(.57f,.34f,.31f,alpha);
                    GUI.Label(new Rect(x+27,78-20*t/.7f+lineIndex*16,resources.width/3-27,24),prefix+(value>0?"+":"−")+Math.Abs(value),label);
                };
                if(i==1){text("装饰 ",delta,0);}
                else text("",delta,0);
                if(i!=0||delta==0)continue;
                var icon=Ui("icons/currency");if(icon==null)continue;
                int count=delta>0?5:3;
                for(int k=0;k<count;k++)
                {
                    float age=t-k*(delta>0?.025f:.03f),duration=delta>0?.46f:.42f;
                    if(age<0||age>duration)continue;
                    float p=age/duration,ease=1-Mathf.Pow(1-p,3);
                    float dx=delta>0?(k-2)*12:(k-1)*20;
                    float dy=delta>0?(k==2?-30:Mathf.Abs(k-2)==1?-25:-14):(k==1?24:16);
                    float size=(delta>0?18:16)*Mathf.Lerp(1,delta>0?.7f:.65f,p);
                    GUI.color=new Color(1,1,1,Mathf.Clamp01((duration-age)/.18f));
                    GUI.DrawTexture(new Rect(x+14+dx*ease-size/2,50+dy*ease-size/2,size,size),icon,ScaleMode.ScaleToFit);
                }
                GUI.color=saved;
            }
            GUI.color=saved;
        }
    }
}
