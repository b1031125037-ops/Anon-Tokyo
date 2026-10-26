using UnityEngine;

namespace AnonTokyo.Shop
{
    public sealed partial class ShopApp
    {
        public AudioSource StageMusic {get;private set;}
        string stageMusicBand="";
        bool stageMusicPaused;
        void TickStageMusic()
        {
            string band=!frontEnd&&Economy.IsPerforming?Economy.performingBand:"";
            if(band=="")
            {if(StageMusic!=null&&stageMusicBand!="")StageMusic.Stop();stageMusicBand="";return;}
            if(StageMusic==null)
            {StageMusic=gameObject.AddComponent<AudioSource>();StageMusic.playOnAwake=false;StageMusic.loop=false;StageMusic.spatialBlend=0;StageMusic.priority=64;}
            if(stageMusicBand!=band)
            {
                StageMusic.Stop();stageMusicBand=band;stageMusicPaused=false;
                StageMusic.clip=Resources.Load<AudioClip>("Audio/Stage/"+(band=="MyGO!!!!!"?"mygo":"mujica"));
                if(StageMusic.clip!=null)
                {StageMusic.time=Mathf.Clamp(StageMusic.clip.length-Economy.performanceRemaining,0,StageMusic.clip.length-.02f);StageMusic.Play();}
            }
            StageMusic.volume=UserSettings.musicVolume;StageMusic.mute=UserSettings.musicMuted;StageMusic.pitch=1;
            bool paused=IsSimulationPaused||quitPrompt||TutorialChoicePending;
            if(paused&&!stageMusicPaused){StageMusic.Pause();stageMusicPaused=true;}
            else if(!paused&&stageMusicPaused){StageMusic.UnPause();stageMusicPaused=false;}
            if(!paused&&StageMusic.clip!=null)
            {
                float expected=Mathf.Clamp(StageMusic.clip.length-Economy.performanceRemaining,0,StageMusic.clip.length-.02f);
                if(Mathf.Abs(StageMusic.time-expected)>.45f)StageMusic.time=expected;
            }
        }
    }
}
