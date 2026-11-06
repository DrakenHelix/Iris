using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Iris {
    public sealed partial class NotchWindow {
        MixerFlowMotion mixerFlow;
        sealed class MixerFlowMotion : IUiMotion {
            readonly NotchWindow owner;public MixerFlowMotion(NotchWindow owner){this.owner=owner;}
            public bool Step(double dt,bool animate){return owner.StepMixerFlow(dt,animate);}
            public void Snap(){Step(0,false);}
        }
        void RetargetMixerFlow(){
            int index=0;foreach(Grid view in mixerRows.Children){var row=(MixerRow)view.Tag;if(row.Present)row.TargetY=index++*36;}
            uiMotion.Wake(mixerFlow);
        }
        bool StepMixerFlow(double dt,bool animate){
            bool active=false;
            foreach(var row in mixerViews.Values.ToArray()){
                double target=row.Present?1:0;
                if(animate){PlayerSpring.Step(ref row.Y,ref row.YVelocity,row.TargetY,dt,.29,1);PlayerSpring.Step(ref row.Alpha,ref row.AlphaVelocity,target,dt,.22,1);}else{row.Y=row.TargetY;row.Alpha=target;row.YVelocity=row.AlphaVelocity=0;}
                bool moving=Math.Abs(row.Y-row.TargetY)>.02||Math.Abs(row.YVelocity)>.05||Math.Abs(row.Alpha-target)>.001||Math.Abs(row.AlphaVelocity)>.01;
                if(!moving&&!row.Present){mixerRows.Children.Remove(row.View);mixerViews.Remove(row.Source.Key);}else active|=moving;
            }
            int physical=0;foreach(Grid view in mixerRows.Children){var row=(MixerRow)view.Tag;row.FlowShift.Y=row.Y-(physical++)*36+4*(1-row.Alpha);view.Opacity=Math.Max(0,Math.Min(1,row.Alpha));Panel.SetZIndex(view,row.Present?1:0);}
            mixerEmpty.Visibility=mixerRows.Children.Count==0?Visibility.Visible:Visibility.Collapsed;UpdateMixerEdges();return active;
        }
    }
}
