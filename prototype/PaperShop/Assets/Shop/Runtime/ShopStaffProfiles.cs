using System.Collections.Generic;

namespace AnonTokyo.Shop
{
    /// <summary>Game balancing inspired by official profiles, never official ability ratings.</summary>
    public static class ShopStaffProfiles
    {
        public static readonly StaffDefinition[] All =
        {
            Make("anon","千早爱音","MyGO!!!!!",65,60,82,1,0,
                "明朗、社交性强，行动力推动乐队前进。项目将这一倾向用于招待。",
                "https://bang-dream.com/artist/mygo/chihaya-anon/"),
            Make("soyo","长崎爽世","MyGO!!!!!",72,58,85,1,0,
                "常倾听他人烦恼、给予支持。项目将这一倾向用于招待。",
                "https://bang-dream.com/artist/mygo/nagasaki-soyo/"),
            Make("taki","椎名立希","MyGO!!!!!",85,80,45,1,0,
                "在 RiNG 打工，照顾伙伴但不擅接客。项目偏向收银与补货。",
                "https://anime.bang-dream.com/avemujica/character/taki/"),
            Make("tomori","高松灯","MyGO!!!!!",62,76,64,1,0,
                "感性独特，喜欢收集石头、叶片等物件。项目偏向整理与补货。",
                "https://bang-dream.com/artist/mygo/takamatsu-tomori/"),
            Make("rana","要乐奈","MyGO!!!!!",55,84,55,1,0,
                "吉他技艺出众，行动随性、喜恶明确。项目给予较高补货操作效率。",
                "https://anime.bang-dream.com/bandorichan/character/mygo/"),
            Make("uika","三角初华","Ave Mujica",86,78,96,3,900,
                "兼任偶像与主唱、负责作词。项目将舞台交流经验转为招待倾向。",
                "https://anime.bang-dream.com/avemujica/character/uika/"),
            Make("mutsumi","若叶睦","Ave Mujica",80,94,65,2,600,
                "口拙但关心他人，自幼练习吉他。项目偏向安静、精细的补货工作。",
                "https://anime.bang-dream.com/avemujica/character/mutsumi/"),
            Make("umiri","八幡海铃","Ave Mujica",90,98,72,2,500,
                "参与多支乐队支援，负责日程等幕后协调。项目偏向补货与收银效率。",
                "https://anime.bang-dream.com/avemujica/character/umiri/"),
            Make("nyamu","祐天寺若麦","Ave Mujica",78,80,98,3,800,
                "以美容视频为主并挑战不同领域，学习表演。项目偏向招待与商品推荐。",
                "https://anime.bang-dream.com/avemujica/character/nyamu/"),
            Make("sakiko","丰川祥子","Ave Mujica",98,82,90,4,1200,
                "创建乐队，重视舞台世界观、言谈有礼。项目偏向收银组织与招待。",
                "https://anime.bang-dream.com/avemujica/character/sakiko/")
        };
        static StaffDefinition Make(string id,string name,string band,int cash,int stock,int welcome,int level,int cost,string profile,string source)
            =>new StaffDefinition {id=id,title=name,band=band,cashier=cash,restocking=stock,hospitality=welcome,
                contractLevel=level,contractCost=cost,initiallyAvailable=cost==0,profile=profile,sourceUrl=source,
                role=welcome>=cash&&welcome>=stock?"招待":cash>=stock?"收银":"补货"};
        public static StaffDefinition Find(string id)=>System.Array.Find(All,s=>s.id==id);
        public static void Ensure(ShopCatalog catalog)
        {
            if(catalog.staff==null)catalog.staff=new List<StaffDefinition>();
            foreach(var template in All)
            {
                var s=catalog.staff.Find(x=>x!=null&&x.id==template.id);
                if(s==null){s=new StaffDefinition{id=template.id};catalog.staff.Add(s);}
                s.title=template.title;s.role=template.role;s.initiallyAvailable=template.initiallyAvailable;
                s.band=template.band;s.cashier=template.cashier;s.restocking=template.restocking;s.hospitality=template.hospitality;
                s.contractLevel=template.contractLevel;s.contractCost=template.contractCost;
                s.profile=template.profile;s.sourceUrl=template.sourceUrl;
            }
            var stage=catalog.Find("stage");if(stage!=null)stage.title="表演舞台";
        }
    }
}
