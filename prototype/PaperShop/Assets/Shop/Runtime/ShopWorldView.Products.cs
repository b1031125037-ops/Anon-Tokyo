using System.Collections.Generic;
using UnityEngine;
namespace AnonTokyo.Shop
{
    public sealed partial class ShopWorldView
    {
        sealed class StockSlot {public Transform root;public string definition,signature="";public int displayed;}
        readonly Dictionary<string,StockSlot> stockSlots=new Dictionary<string,StockSlot>();
        public int DisplayedProductCount(string id)=>stockSlots.TryGetValue(id,out var slot)?slot.displayed:0;
        public void SyncProductDisplays(ShopEconomy economy)
        {
            foreach(var pair in stockSlots)
            {
                var shelf=economy.Shelf(pair.Key);var slot=pair.Value;
                var product=shelf==null?null:catalog.products.Find(p=>p.id==shelf.productId);
                int max=slot.definition=="shoes"||slot.definition=="head"||slot.definition=="bag"?3:1;
                int count=product?.modelPrefab==null?0:Mathf.Min(max,shelf.quantity);
                string signature=(product?.id??"")+":"+count;
                if(slot.signature==signature)continue;slot.signature=signature;slot.displayed=count;
                foreach(Transform child in slot.root){child.gameObject.SetActive(false);Destroy(child.gameObject);}
                if(count==0)continue;
                var fixture=catalog.Find(slot.definition).modelPrefab.GetComponentInChildren<MeshFilter>();
                Vector3 furniture=Vector3.Scale(fixture.sharedMesh.bounds.size,fixture.transform.localScale);
                for(int i=0;i<count;i++)
                {
                    var item=Instantiate(product.modelPrefab,slot.root);item.name="Stock "+product.id+" "+i;
                    var mesh=item.GetComponentInChildren<MeshFilter>().sharedMesh;
                    Vector3 limit=new Vector3(furniture.x*.74f,furniture.y*.58f,furniture.z*.8f),position=Vector3.zero;
                    bool hanging=slot.definition=="top"||slot.definition=="bottom"||slot.definition=="outfit"||slot.definition=="accessory";
                    if(slot.definition=="top")position.y=furniture.y*.81f;
                    else if(slot.definition=="bottom"){position.y=furniture.y*.93f;limit.y=furniture.y*.57f;}
                    else if(slot.definition=="outfit"){position.y=furniture.y*.94f;limit.y=furniture.y*.62f;}
                    else if(slot.definition=="accessory"){position=new Vector3(0,furniture.y*.94f,-furniture.z*.04f);limit=new Vector3(furniture.x*.4f,furniture.y*.37f,.07f);}
                    else if(slot.definition=="head")
                    {position=new Vector3((i==0?0:i==1?-.24f:.24f)*furniture.x,(i==0?.96f:i==1?.70f:.47f)*furniture.y,0);limit=new Vector3(furniture.x*.30f,furniture.y*.19f,furniture.z*.70f);}
                    else if(slot.definition=="shoes")
                    {position.y=(i==0?.12f:i==1?.40f:.69f)*furniture.y;limit=new Vector3(furniture.x*.79f,furniture.y*.23f,furniture.z*.80f);}
                    else if(slot.definition=="bag")
                    {position=new Vector3((i-1)*furniture.x*.32f,furniture.y*.18f,0);limit=new Vector3(furniture.x*.25f,furniture.y*.42f,furniture.z*.8f);}
                    float scale=Mathf.Min(limit.x/mesh.bounds.size.x,limit.y/mesh.bounds.size.y,limit.z/mesh.bounds.size.z);
                    if(hanging)position.y-=mesh.bounds.size.y*scale;
                    item.transform.localScale=Vector3.one*scale;item.transform.localPosition=position;item.transform.localRotation=Quaternion.identity;
                }
            }
        }
    }
}
