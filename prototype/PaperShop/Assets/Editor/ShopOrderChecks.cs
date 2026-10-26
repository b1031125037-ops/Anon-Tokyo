using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using AnonTokyo.Shop;
public static class ShopOrderChecks
{
 [Serializable] sealed class Report {public bool passed;public int checks;public string[] results;}
 public static void Run(ShopCatalog catalog,string output)
 {
        catalog=ShopLedgerFixture.Catalog(catalog);
  Directory.CreateDirectory(output);var results=new List<string>();
  Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("ShopOrderChecks FAILED: "+label);results.Add(label);};

  var map=ShopProfiles.NewGame(catalog,false).layout;
        ShopLedgerFixture.Reset(catalog);
  var e=ShopEconomy.CreateDefault(catalog,map);e.cash=2500;e.Contract(catalog,"top_tshirt",out _);
  check(e.Order(catalog,"top_tshirt",out _)&&e.cash==2320&&e.orders[0].dispatchRemaining==1&&e.orders[0].remaining==3,"Basic goods: one second dispatch, three second transport, correct cost");
  string id=e.orders[0].orderId;
  check(e.CancelOrder(id,out _)&&e.cash==2420&&e.WarehouseCommitted==0,"Pending order refunds paid cost and releases capacity");
  check(!e.CancelOrder(id,out _)&&e.cash==2420,"Cancellation cannot refund twice");
  e.Order(catalog,"top_tshirt",out _);e.Tick(.25f);
  string path=Path.Combine(output,"pending.json");ShopSession.SaveToPath(map,e,catalog,path);
  var loaded=ShopSession.LoadFromPath(catalog,path,out _);
  check(loaded!=null&&loaded.economy.orders[0].dispatchRemaining==.75f,"Pending delivery retains time across reload");
  e.Tick(.75f);
  check(!e.orders[0].awaitingDispatch&&e.orders[0].remaining==3,"Dispatch transition does not consume transport time");
  string before=JsonUtility.ToJson(e);
  check(!e.CancelOrder(e.orders[0].orderId,out _)&&before==JsonUtility.ToJson(e),"Shipped order cannot be cancelled");
  e.Tick(1f);path=Path.Combine(output,"shipping.json");ShopSession.SaveToPath(map,e,catalog,path);loaded=ShopSession.LoadFromPath(catalog,path,out _);
  check(loaded!=null&&loaded.economy.orders[0].remaining==2,"Shipping time survives reload");
  loaded.economy.Tick(2f);
  check(loaded.economy.orders.Count==0&&loaded.economy.Stock("top_tshirt").available==20&&loaded.economy.Validate(catalog,map,out _),"Reloaded shipment arrives exactly once and conserves inventory");
  loaded.economy.Tick(100);check(loaded.economy.Stock("top_tshirt").available==20,"No duplicate arrival");
  e.Order(catalog,"shoes_canvas",out _);check(e.orders.Count==1,"Uncontracted product cannot create delivery");
  e.Order(catalog,"top_tshirt",out _);e.Tick(.5f);e.Order(catalog,"top_tshirt",out _);
  check(e.orders[1].orderId!=e.orders[2].orderId,"Repeated orders have independent identities");
  id=e.orders[2].orderId;check(e.CancelOrder(id,out _)&&e.orders.Count==2,"Cancellation leaves sibling orders intact");
  e.Tick(100);check(e.Stock("top_tshirt").available==40&&e.Validate(catalog,map,out _),"Long frame completes multiple shipments once");
  var sandbox=ShopProfiles.NewGame(catalog,true);var free=sandbox.economy;int money=free.cash;
        ShopLedgerFixture.Reset(catalog);
  free.Order(catalog,"top_tshirt",out _);id=free.orders[0].orderId;
  check(free.orders[0].paidAmount==0&&free.CancelOrder(id,out _)&&free.cash==money,"Free purchase cannot create a paid refund");
  free.Order(catalog,"top_tshirt",out _);free.Tick(2);
  check(free.orders.Count==0&&free.Validate(catalog,sandbox.layout,out _),"Infinite mode has two-second quick delivery");
  var full=ShopEconomy.CreateDefault(catalog,map);full.cash=10000;full.Contract(catalog,"top_tshirt",out _);
  for(int i=0;i<10;i++)check(full.Order(catalog,"top_tshirt",out _),"Reserve warehouse batch "+i);
  before=JsonUtility.ToJson(full);check(!full.Order(catalog,"top_tshirt",out _)&&before==JsonUtility.ToJson(full),"Reserved incoming stock prevents warehouse overflow atomically");
  check(full.CancelOrder(full.orders[0].orderId,out _)&&full.Order(catalog,"top_tshirt",out _),"Cancelled capacity can be reused once");

  File.WriteAllText(Path.Combine(output,"order-verification.json"),JsonUtility.ToJson(new Report{passed=true,checks=results.Count,results=results.ToArray()},true));
  Debug.Log("ShopOrderChecks_OK "+results.Count);
 }
}
