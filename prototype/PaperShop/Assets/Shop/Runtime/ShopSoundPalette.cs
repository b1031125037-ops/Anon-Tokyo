using System;
using UnityEngine;

namespace AnonTokyo.Shop
{
    // Short original cartoon Foley: pitch bends, soft transients and staggered mallets.
    public static class ShopSoundPalette
    {
        public static readonly string[] Keys={"hover","click","select","payment","income","purchase","place","rotate","move","pickup","stock","upgrade","open","close","closed","delivery","save","error"};
        public static float[] Samples(string key, int rate=44100)
        {
            if(key=="payment"||key=="income"||key=="pickup"||key=="stock")return Foley(key,rate);
            float duration=key=="upgrade"?.48f:key=="open"?.42f:key=="close"?.32f:key=="income"?.28f:key=="payment"?.22f:.16f;
            float[] data=new float[(int)(rate*duration)];
            var random=new System.Random(71);double phase=0;
            for(int i=0;i<data.Length;i++)
            {
                float t=i/(float)rate,u=t/duration;
                float f=key=="hover"?Mathf.Lerp(700,370,u):key=="click"?Mathf.Lerp(320,870,Mathf.Min(1,u*3)):
                    key=="select"?Mathf.Lerp(510,980,u):key=="rotate"?480:key=="move"?300:key=="place"?180:
                    key=="pickup"?340:key=="stock"?230:key=="close"?Mathf.Lerp(480,170,u):key=="closed"?250:
                    key=="error"?Mathf.Lerp(260,130,u):600;
                phase+=2*Math.PI*f/rate;
                float envelope=Mathf.Min(1,t/.004f)*Mathf.Pow(1-u,2.5f);
                float value=(float)(Math.Sin(phase)+.2*Math.Sin(phase*2))*envelope*.42f;
                if(key=="pickup"||key=="stock"||key=="purchase")value+=((float)random.NextDouble()*2-1)*envelope*.13f;
                if(key=="payment"||key=="income"||key=="upgrade"||key=="open"||key=="delivery"||key=="save")
                {
                    value=0;
                    int notes=key=="upgrade"?4:key=="open"?3:2;
                    for(int n=0;n<notes;n++)
                    {
                        float local=t-n*.075f;if(local<0)continue;
                        float frequency=(key=="payment"?1046:key=="income"?1318:key=="open"?660:784)*Mathf.Pow(1.25f,n);
                        float e=Mathf.Min(1,local/.003f)*Mathf.Exp(-local*17)*Mathf.Min(1,(duration-t)/.025f);
                        value+=e*.33f*(Mathf.Sin(2*Mathf.PI*frequency*local)+.17f*Mathf.Sin(2*Mathf.PI*frequency*2.76f*local));
                    }
                }
                data[i]=Mathf.Clamp(value,-.8f,.8f);
            }
            return data;
        }
        static float[] Foley(string key,int rate)
        {
            bool cloth=key=="stock"||key=="pickup";
            float duration=key=="income"?.68f:key=="payment"?.22f:key=="stock"?.48f:.34f;
            var samples=new float[(int)(duration*rate)];var rng=new System.Random(198);
            float low=0,mid=0,peak=0;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)rate,n=(float)rng.NextDouble()*2-1;
                low+=.035f*(n-low);mid+=.18f*(n-mid);float value=0;
                if(cloth)
                {
                    // Filtered fabric rustle and low, padded impacts; no pitched bell or hard click.
                    int layers=key=="stock"?3:2;
                    for(int j=0;j<layers;j++)
                    {
                        float local=t-j*(key=="stock"?.105f:.095f);if(local<0)continue;
                        float e=Mathf.Min(1,local/.012f)*Mathf.Exp(-local*22);
                        float thump=Mathf.Sin(2*Mathf.PI*(95-j*12)*local)*Mathf.Exp(-local*38);
                        value+=(low*2.4f+mid*.16f)*e+thump*.24f*Mathf.Min(1,local/.008f);
                    }
                }
                else if(key=="payment")
                {
                    // Mechanical register keys, before the drawer and cash bell.
                    for(int press=0;press<3;press++)
                    {
                        float local=t-press*.065f;if(local<0)continue;
                        value+=(mid*.5f+Mathf.Sin(2*Mathf.PI*310*local)*.15f)*Mathf.Exp(-local*100)*Mathf.Min(1,local/.0015f);
                    }
                }
                else
                {
                    float slide=Mathf.Clamp01((t-.01f)/.015f)*Mathf.Clamp01((.23f-t)/.13f);
                    value+=mid*.42f*slide;
                    float hit=t-.035f;if(hit>=0)value+=(low*.6f+Mathf.Sin(2*Mathf.PI*165*hit)*.2f)*Mathf.Exp(-hit*38)*Mathf.Min(1,hit/.003f);
                    float bell=t-.085f;
                    if(bell>=0)
                    {
                        float e=Mathf.Min(1,bell/.002f)*Mathf.Exp(-bell*7);
                        value+=e*(.3f*Mathf.Sin(2*Mathf.PI*1760*bell)+.12f*Mathf.Sin(2*Mathf.PI*2637*bell)+.05f*Mathf.Sin(2*Mathf.PI*4141*bell));
                    }
                    float latch=t-.28f;if(latch>=0)value+=mid*.23f*Mathf.Exp(-latch*65)*Mathf.Min(1,latch/.002f);
                }
                value*=Mathf.Clamp01((duration-t)/.035f);
                samples[i]=value;peak=Mathf.Max(peak,Mathf.Abs(value));
            }
            float target=cloth?.66f:.74f;
            if(peak>0)for(int i=0;i<samples.Length;i++)samples[i]*=target/peak;
            return samples;
        }
    }
}
