using System;

namespace AnonTokyo.Shop
{
    /// <summary>Read-only presentation observer: no gameplay writes, no load-time windfall.</summary>
    public sealed class ShopResourceFeedback
    {
        public readonly long[] displayed=new long[3];
        public readonly float[] started={-100,-100,-100};
        public long displayedCapacity;
        readonly long[] pending=new long[3];
        readonly float[] queued={-1,-1,-1};
        long cash,stock,capacity,popularity,pendingCapacity;
        bool ready,infinite;
        public void Reset(int money,int occupied,int maximum,int people,bool unlimited)
        {
            cash=money;stock=occupied;capacity=maximum;popularity=people;infinite=unlimited;ready=true;
            pendingCapacity=displayedCapacity=0;
            for(int i=0;i<3;i++){displayed[i]=pending[i]=0;started[i]=-100;queued[i]=-1;}
        }
        void Queue(int index,long delta,float time)
        {
            if(delta==0)return;
            pending[index]+=delta;if(queued[index]<0)queued[index]=time;
        }
        public void Observe(float time,int money,int occupied,int maximum,int people,bool unlimited)
        {
            if(!ready||infinite!=unlimited){Reset(money,occupied,maximum,people,unlimited);return;}
            if(!infinite)
            {
                Queue(0,(long)money-cash,time);Queue(1,(long)occupied-stock,time);
                long diff=(long)maximum-capacity;
                pendingCapacity+=diff;if(diff!=0&&queued[1]<0)queued[1]=time;
            }
            Queue(2,(long)people-popularity,time);
            cash=money;stock=occupied;capacity=maximum;popularity=people;
            for(int i=0;i<3;i++)
            {
                if(queued[i]<0||time-queued[i]<.1f)continue;
                if(pending[i]!=0||(i==1&&pendingCapacity!=0))
                {
                    displayed[i]=pending[i];started[i]=time;
                    if(i==1)displayedCapacity=pendingCapacity;
                }
                pending[i]=0;queued[i]=-1;if(i==1)pendingCapacity=0;
            }
        }
    }
}
