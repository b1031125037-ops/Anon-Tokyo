using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using AnonTokyo.Shop;
public static class ShopRestockSettingsChecks
{
 [Serializable] sealed class Report {public bool passed;public int checks;public string[] results;}
 public static void Run(ShopCatalog catalog,string output)
 {
  Directory.CreateDirectory(output);var results=new List<string>();
  Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("ShopRestockSettingsChecks FAILED: "+label);results.Add(label);};

  var setup=ShopProfiles.NewGame(catalog,true);var layout=setup.layout;var e=setup.economy;
  e.shopOpen=false;e.CancelRestock();
  foreach(var s in e.shelves){e.Stock(s.productId).purchased+=10-s.quantity;s.quantity=10;}
  foreach(var ratio in new[]{0f,.25f,.75f,1f})check(!e.SetShelfRestockThreshold("top-1",ratio,out _),"Reject obsolete editable threshold "+ratio);
  check(e.SetShelfRestockThreshold("top-1",null,out _)&&e.EffectiveRestockThreshold("top-1")==.5f,"Fixed threshold independent of old inheritance");
  var shelf=e.Shelf("top-1");var stock=e.Stock(shelf.productId);
  stock.available+=shelf.quantity-5;shelf.quantity=5;
  check(e.NextRestock(catalog,layout)==null,"Exactly 50 percent never triggers");
  stock.available++;shelf.quantity--;
  check(e.NextRestock(catalog,layout)==shelf.instanceId,"Below 50 percent triggers");
  check(e.BeginRestockBatch(catalog,layout,30,shelf.instanceId,out _)&&e.jobs.Count==1&&e.job.quantity==6,"Only the low-stock shelf is reserved; full neighbor excluded");
  check(e.Pickup(out _)&&e.CompleteRestock(catalog,layout,out _)&&shelf.quantity==10&&e.Validate(catalog,layout,out _),"Replenishment fills capacity without loss");
  check(e.UpgradeShelf(catalog,layout,shelf.instanceId,out _)&&e.NextRestock(catalog,layout)==null,"10 of 20 remains exactly half after upgrade");
  stock.available++;shelf.quantity--;
  check(e.NextRestock(catalog,layout)==shelf.instanceId,"9 of 20 triggers after upgrade");
  string path=Path.Combine(output,"fixed-threshold.json");ShopSession.SaveToPath(layout,e,catalog,path);
  var loaded=ShopSession.LoadFromPath(catalog,path,out _);
  check(loaded!=null&&loaded.economy.EffectiveRestockThreshold(shelf.instanceId)==.5f,"Fixed behavior survives save/load");
  e.autoRestockThreshold=.9f;shelf.overrideRestockThreshold=true;shelf.restockThreshold=.1f;e.MigrateManagementRules();
  check(e.autoRestockThreshold==.5f&&!shelf.overrideRestockThreshold&&e.EffectiveRestockThreshold(shelf.instanceId)==.5f,"Old threshold data is normalized");

  File.WriteAllText(Path.Combine(output,"restock-settings-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
  Debug.Log("ShopRestockSettingsChecks_OK "+results.Count);
 }
}
