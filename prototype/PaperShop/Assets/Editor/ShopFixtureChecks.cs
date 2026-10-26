using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using AnonTokyo.Shop;
public static class ShopFixtureChecks
{
 [Serializable] sealed class Report {public bool passed;public int checks;public string[] results;}
 public static void Run(ShopCatalog catalog,string output)
 {
  Directory.CreateDirectory(output);var results=new List<string>();
  Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("ShopFixtureChecks FAILED: "+label);results.Add(label);};

  for(int level=1;level<=5;level++)
  {
   var setup=ShopProfiles.NewGame(catalog,false);setup.economy.EnableTestResources();var e=setup.economy;var layout=setup.layout;e.shopLevel=level;
   foreach(var id in ShopProductSet.Categories)
   {
    int i=Array.IndexOf(ShopProductSet.Categories,id);
    int expected=level==1?1:level==2?(i<3?2:1):level==3?2:level==4?3:4;
    check(e.FixtureCategoryLimit(id)==expected,"Level "+level+" category "+id+" cap "+expected);
    while(e.OwnedCategoryCount(catalog,id)<expected)check(e.BuyFixture(catalog,layout,id,out _,out _),"Purchase within "+id+" allowance");
    string before=JsonUtility.ToJson(e);
    check(!e.BuyFixture(catalog,layout,id,out _,out _)&&before==JsonUtility.ToJson(e),"Infinite resources cannot bypass "+id+" cap");
   }
   check(e.FixtureLimit==new[]{7,10,14,21,28}[level-1],"Total allowance at level "+level);
   check(e.Validate(catalog,layout,out _),"Purchased category ledgers valid "+level);
  }
  var state=ShopProfiles.NewGame(catalog,false);var economy=state.economy;var map=state.layout;
  check(economy.StoreFixture(catalog,map,"top-1",out _),"Store existing display");
  string snapshot=JsonUtility.ToJson(economy);
  check(!economy.BuyFixture(catalog,map,"top",out _,out _)&&snapshot==JsonUtility.ToJson(economy),"Storage cannot release a category allowance");
  check(economy.BuyFixture(catalog,map,"head",out string idNew,out _)&&economy.cash==500,"Finite purchase charges exactly 300");
  var stored=economy.StoredFixture(idNew);stored.shelf.level=2;
  var oldTop=ShopModel.CreateDefault().facilities.Find(f=>f.instanceId=="top-1");
  oldTop.instanceId="top-1";
  // Place using the actual freed default-store location, then ensure storage ownership is transferred once.
  var original=ShopProfiles.NewGame(catalog,false).layout.facilities.Find(f=>f.instanceId=="top-1");
  check(economy.PlaceStoredFixture(catalog,map,original,out _)&&economy.StoredFixture("top-1")==null,"Stored display can return to its freed cell");
  check(!economy.PlaceStoredFixture(catalog,map,original,out _),"Cannot place the same stored instance twice");
  string path=Path.Combine(output,"fixtures.json");ShopSession.SaveToPath(map,economy,catalog,path);
  var loaded=ShopSession.LoadFromPath(catalog,path,out _);
  check(loaded!=null&&loaded.economy.StoredFixture(idNew).shelf.level==2&&loaded.economy.OwnedCategoryCount(catalog,"top")==1,"Category ownership and upgraded storage survive reload");
  var corrupt=JsonUtility.FromJson<ShopEconomy>(JsonUtility.ToJson(economy));corrupt.storedFixtures.Add(corrupt.storedFixtures[0]);
  check(!corrupt.Validate(catalog,map,out _),"Duplicate stored instance invalidates save");
  economy.popularity=9;economy.RecordSuccessfulVisit();
  check(economy.ShopLevel==1&&economy.popularity==10&&economy.PopularityCap==20,"Popularity qualifies upgrade without automatically granting a free level");
  economy.shopLevel=5;economy.popularity=100;economy.RefreshShopLevel();economy.RecordSuccessfulVisit();
  check(economy.ShopLevel==5&&economy.popularity==100,"Maximum level cannot exceed one hundred popularity");

  var upgrade=ShopProfiles.NewGame(catalog,false);var ue=upgrade.economy;
  ue.cash=599;ue.popularity=10;ue.RefreshShopLevel();
  string originalUpgrade=JsonUtility.ToJson(ue),originalMap=JsonUtility.ToJson(upgrade.layout);
  check(!ue.TryExpandShop(catalog,upgrade.layout,out _)&&originalUpgrade==JsonUtility.ToJson(ue)&&originalMap==JsonUtility.ToJson(upgrade.layout),"Qualified popularity cannot upgrade without full coin cost; no state changes");
  ue.cash=600;
  check(ue.TryExpandShop(catalog,upgrade.layout,out _)&&ue.cash==0&&ue.ShopLevel==2&&ue.PopularityCap==40&&upgrade.layout.width==10,"Confirmation consumes exact coins and commits level, cap and area together");
  originalUpgrade=JsonUtility.ToJson(ue);
  check(!ue.TryExpandShop(catalog,upgrade.layout,out _)&&originalUpgrade==JsonUtility.ToJson(ue),"Double upgrade confirmation cannot bypass the next popularity and money gates");
  var feedback=new ShopResourceFeedback();
  feedback.Observe(0,100,10,100,0,false);
  check(feedback.started[0]<0,"Initial load never animates a resource windfall");
  feedback.Observe(1,110,9,120,1,false);feedback.Observe(1.05f,125,8,120,2,false);
  check(feedback.started[0]<0,"Resource changes are batched for one hundred milliseconds");
  feedback.Observe(1.2f,125,8,120,2,false);
  check(feedback.displayed[0]==25&&feedback.displayed[1]==-2&&feedback.displayedCapacity==20&&feedback.displayed[2]==2,"Batch preserves coin, popularity, occupied stock and capacity deltas");
  feedback.Observe(2,95,8,120,2,false);feedback.Observe(2.2f,95,8,120,2,false);
  check(feedback.displayed[0]==-30,"Spending produces a real negative delta");
  feedback.Reset(999,99,300,9,false);feedback.Observe(3,999,99,300,9,false);
  check(feedback.started[0]<0&&feedback.started[1]<0&&feedback.started[2]<0,"Reload resets in-flight animations without fabricated income");
  feedback.Observe(4,1,1,100,9,true);feedback.Observe(5,900,30,300,10,true);feedback.Observe(5.2f,900,30,300,10,true);
  check(feedback.started[0]<0&&feedback.started[1]<0&&feedback.displayed[2]==1,"Infinite mode omits fictitious cash and storage changes while retaining popularity feedback");
  feedback.Reset(100,10,100,0,false);feedback.Observe(6,110,10,100,0,false);feedback.Observe(6.05f,100,10,100,0,false);feedback.Observe(6.2f,100,10,100,0,false);
  check(feedback.started[0]<0,"Opposing changes with net zero do not animate a fake transaction");
  File.WriteAllText(Path.Combine(output,"fixture-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
  Debug.Log("ShopFixtureChecks_OK "+results.Count);
 }
}
