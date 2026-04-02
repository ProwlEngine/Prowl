using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

using SysColor = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

public static class ColorPicker
{
    private const float SVSize = 180f;
    private const float HueBarWidth = 20f;
    private const float AlphaBarWidth = 20f;
    private const float SliderHeight = 16f;

    public static void Draw(Paper paper, string id, Prowl.Vector.Color value, Action<Prowl.Vector.Color> onChange)
    {
        var font = EditorTheme.DefaultFont;
        float fontSize = EditorTheme.FontSize - 1;

        using (paper.Column(id)
            .Width(280)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.PanelBackground)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .Rounded(6)
            .ChildLeft(8).ChildRight(8).ChildTop(8).ChildBottom(8)
            .RowBetween(6)
            .Layer(Layer.Topmost)
            .Enter())
        {
            var el = paper.CurrentParent;

            // Init HSV from value on first frame
            float h = paper.GetElementStorage(el, "h", -1f);
            if (h < 0) { ColorToHSV(value, out h, out float si, out float vi); paper.SetElementStorage(el, "h", h); paper.SetElementStorage(el, "s", si); paper.SetElementStorage(el, "v", vi); paper.SetElementStorage(el, "a", value.A); }
            float s = paper.GetElementStorage(el, "s", 1f);
            float v = paper.GetElementStorage(el, "v", 1f);
            float a = paper.GetElementStorage(el, "a", 1f);

            // === SV Square + Hue + Alpha ===
            using (paper.Row($"{id}_top").Height(SVSize).RowBetween(6).Enter())
            {
                // SV square
                DrawSVSquare(paper, $"{id}_sv", el, h, s, v, a, onChange);
                // Hue bar
                DrawHueBar(paper, $"{id}_hue", el, h, s, v, a, onChange);
                // Alpha bar
                DrawAlphaBar(paper, $"{id}_alpha", el, h, s, v, a, onChange);
            }

            // === Preview ===
            using (paper.Row($"{id}_prev").Height(24).RowBetween(6).Enter())
            {
                paper.Box($"{id}_old").Size(40, 24).Rounded(3).BorderColor(EditorTheme.Border).BorderWidth(1)
                    .BackgroundColor(SysColor.FromArgb((int)(value.A*255),(int)(value.R*255),(int)(value.G*255),(int)(value.B*255)));
                var nc = HSVToColor(h, s, v, a);
                paper.Box($"{id}_new").Size(40, 24).Rounded(3).BorderColor(EditorTheme.Border).BorderWidth(1)
                    .BackgroundColor(SysColor.FromArgb((int)(nc.A*255),(int)(nc.R*255),(int)(nc.G*255),(int)(nc.B*255)));
                if (font != null)
                {
                    int ri=(int)(nc.R*255), gi=(int)(nc.G*255), bi=(int)(nc.B*255);
                    paper.Box($"{id}_hex").Width(UnitValue.Stretch()).Height(24).ChildLeft(4).IsNotInteractable()
                        .Text($"#{ri:X2}{gi:X2}{bi:X2}{(int)(a*255):X2}", font).TextColor(EditorTheme.Text).FontSize(fontSize);
                }
            }

            // === RGBA sliders ===
            var c = HSVToColor(h, s, v, a);
            ChannelSlider(paper, $"{id}_r", "R", c.R, SysColor.FromArgb(255,200,60,60), font, fontSize, nr =>
            { var nc2 = new Prowl.Vector.Color(nr,c.G,c.B,c.A); SyncHSV(paper,el,nc2); onChange(nc2); });
            ChannelSlider(paper, $"{id}_g", "G", c.G, SysColor.FromArgb(255,60,200,60), font, fontSize, ng =>
            { var nc2 = new Prowl.Vector.Color(c.R,ng,c.B,c.A); SyncHSV(paper,el,nc2); onChange(nc2); });
            ChannelSlider(paper, $"{id}_b", "B", c.B, SysColor.FromArgb(255,60,60,200), font, fontSize, nb =>
            { var nc2 = new Prowl.Vector.Color(c.R,c.G,nb,c.A); SyncHSV(paper,el,nc2); onChange(nc2); });
            ChannelSlider(paper, $"{id}_a2", "A", a, EditorTheme.Text, font, fontSize, na =>
            { paper.SetElementStorage(el,"a",na); onChange(HSVToColor(paper.GetElementStorage(el,"h",h),paper.GetElementStorage(el,"s",s),paper.GetElementStorage(el,"v",v),na)); });
        }
    }

    static void DrawSVSquare(Paper paper, string id, ElementHandle el, float h, float s, float v, float a, Action<Prowl.Vector.Color> onChange)
    {
        paper.Box(id).Size(SVSize, SVSize).Rounded(3)
            .OnClick(e => { SetSV(paper,el,e,h,a,onChange); })
            .OnDragging(e => { SetSV(paper,el,e,h,a,onChange); })
            .OnPostLayout((handle, rect) => paper.AddActionElement(ref handle, (canvas, r) =>
            {
                float x=(float)r.Min.X, y=(float)r.Min.Y, w=(float)r.Size.X, ht=(float)r.Size.Y;
                var hc = HSVToColor32(h,1,1);
                canvas.RoundedRectFilled(x,y,w,ht,3,3,3,3,hc);
                canvas.SetLinearBrush(x,y+ht/2,x+w,y+ht/2, Color32.FromArgb(255,255,255,255), Color32.FromArgb(0,255,255,255));
                canvas.RoundedRectFilled(x,y,w,ht,3,3,3,3, Color32.FromArgb(255,255,255,255));
                canvas.ClearBrush();
                canvas.SetLinearBrush(x+w/2,y,x+w/2,y+ht, Color32.FromArgb(0,0,0,0), Color32.FromArgb(255,0,0,0));
                canvas.RoundedRectFilled(x,y,w,ht,3,3,3,3, Color32.FromArgb(255,255,255,255));
                canvas.ClearBrush();
                float cx=x+s*w, cy=y+(1f-v)*ht;
                canvas.SetStrokeColor(Color32.FromArgb(255,255,255,255)); canvas.SetStrokeWidth(2);
                canvas.BeginPath(); canvas.Circle(cx,cy,5,16); canvas.Stroke();
                canvas.SetStrokeColor(Color32.FromArgb(255,0,0,0)); canvas.SetStrokeWidth(1);
                canvas.BeginPath(); canvas.Circle(cx,cy,6,16); canvas.Stroke();
            }));
    }

    static void SetSV(Paper paper, ElementHandle el, PaperUI.Events.ElementEvent e, float h, float a, Action<Prowl.Vector.Color> onChange)
    {
        float ns = Math.Clamp((float)e.NormalizedPosition.X, 0, 1);
        float nv = 1f - Math.Clamp((float)e.NormalizedPosition.Y, 0, 1);
        paper.SetElementStorage(el, "s", ns);
        paper.SetElementStorage(el, "v", nv);
        onChange(HSVToColor(paper.GetElementStorage(el,"h",h), ns, nv, paper.GetElementStorage(el,"a",a)));
    }

    static void DrawHueBar(Paper paper, string id, ElementHandle el, float h, float s, float v, float a, Action<Prowl.Vector.Color> onChange)
    {
        paper.Box(id).Size(HueBarWidth, SVSize).Rounded(3)
            .OnClick(e => { SetHue(paper,el,e,s,v,a,onChange); })
            .OnDragging(e => { SetHue(paper,el,e,s,v,a,onChange); })
            .OnPostLayout((handle, rect) => paper.AddActionElement(ref handle, (canvas, r) =>
            {
                float x=(float)r.Min.X, y=(float)r.Min.Y, w=(float)r.Size.X, ht=(float)r.Size.Y;
                int segs=12; float segH=ht/segs;
                for(int i=0;i<segs;i++)
                {
                    var c1=HSVToColor32((float)i/segs*360,1,1);
                    var c2=HSVToColor32((float)(i+1)/segs*360,1,1);
                    canvas.SetLinearBrush(x+w/2,y+i*segH,x+w/2,y+(i+1)*segH,c1,c2);
                    canvas.RectFilled(x,y+i*segH,w,segH+1,Color32.FromArgb(255,255,255,255));
                    canvas.ClearBrush();
                }
                float cy=y+(h/360f)*ht;
                canvas.SetStrokeColor(Color32.FromArgb(255,255,255,255)); canvas.SetStrokeWidth(2);
                canvas.BeginPath(); canvas.Rect(x,cy-2,w,4); canvas.Stroke();
            }));
    }

    static void SetHue(Paper paper, ElementHandle el, PaperUI.Events.ElementEvent e, float s, float v, float a, Action<Prowl.Vector.Color> onChange)
    {
        float nh = Math.Clamp((float)e.NormalizedPosition.Y, 0, 1) * 360f;
        paper.SetElementStorage(el, "h", nh);
        onChange(HSVToColor(nh, paper.GetElementStorage(el,"s",s), paper.GetElementStorage(el,"v",v), paper.GetElementStorage(el,"a",a)));
    }

    static void DrawAlphaBar(Paper paper, string id, ElementHandle el, float h, float s, float v, float a, Action<Prowl.Vector.Color> onChange)
    {
        paper.Box(id).Size(AlphaBarWidth, SVSize).Rounded(3)
            .OnClick(e => { SetAlpha(paper,el,e,h,s,v,onChange); })
            .OnDragging(e => { SetAlpha(paper,el,e,h,s,v,onChange); })
            .OnPostLayout((handle, rect) => paper.AddActionElement(ref handle, (canvas, r) =>
            {
                float x=(float)r.Min.X, y=(float)r.Min.Y, w=(float)r.Size.X, ht=(float)r.Size.Y;
                var col = HSVToColor32(h,s,v);
                var colT = Color32.FromArgb(0,col.R,col.G,col.B);
                canvas.SetLinearBrush(x+w/2,y,x+w/2,y+ht,col,colT);
                canvas.RoundedRectFilled(x,y,w,ht,3,3,3,3,Color32.FromArgb(255,255,255,255));
                canvas.ClearBrush();
                float cy=y+(1f-a)*ht;
                canvas.SetStrokeColor(Color32.FromArgb(255,255,255,255)); canvas.SetStrokeWidth(2);
                canvas.BeginPath(); canvas.Rect(x,cy-2,w,4); canvas.Stroke();
            }));
    }

    static void SetAlpha(Paper paper, ElementHandle el, PaperUI.Events.ElementEvent e, float h, float s, float v, Action<Prowl.Vector.Color> onChange)
    {
        float na = 1f - Math.Clamp((float)e.NormalizedPosition.Y, 0, 1);
        paper.SetElementStorage(el, "a", na);
        onChange(HSVToColor(paper.GetElementStorage(el,"h",h), paper.GetElementStorage(el,"s",s), paper.GetElementStorage(el,"v",v), na));
    }

    static void ChannelSlider(Paper paper, string id, string label, float value, SysColor labelColor, FontFile? font, float fontSize, Action<float> onChange)
    {
        using (paper.Row(id).Height(SliderHeight).RowBetween(4).Enter())
        {
            if (font != null) paper.Box($"{id}_l").Width(14).IsNotInteractable().Text(label, font).TextColor(labelColor).FontSize(fontSize);
            paper.Box($"{id}_t").Height(SliderHeight).Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.InputBackground).Rounded(2)
                .OnClick(e => onChange(Math.Clamp((float)e.NormalizedPosition.X, 0, 1)))
                .OnDragging(e => onChange(Math.Clamp((float)e.NormalizedPosition.X, 0, 1)))
                .OnPostLayout((handle, rect) => paper.AddActionElement(ref handle, (canvas, r) =>
                {
                    float fillW = (float)(r.Size.X * Math.Clamp(value, 0, 1));
                    if (fillW > 0)
                        canvas.RoundedRectFilled((float)r.Min.X,(float)r.Min.Y,fillW,(float)r.Size.Y,2,0,0,2,
                            new Prowl.Vector.Color(labelColor.R/255f,labelColor.G/255f,labelColor.B/255f,0.7f));
                }));
            if (font != null) paper.Box($"{id}_v").Width(28).IsNotInteractable().Text($"{(int)(value*255)}", font).TextColor(EditorTheme.Text).FontSize(fontSize);
        }
    }

    static void SyncHSV(Paper paper, ElementHandle el, Prowl.Vector.Color c)
    {
        ColorToHSV(c, out float h, out float s, out float v);
        paper.SetElementStorage(el, "h", h);
        paper.SetElementStorage(el, "s", s);
        paper.SetElementStorage(el, "v", v);
        paper.SetElementStorage(el, "a", c.A);
    }

    static void ColorToHSV(Prowl.Vector.Color c, out float h, out float s, out float v)
    {
        float r=c.R, g=c.G, b=c.B;
        float max=MathF.Max(r,MathF.Max(g,b)), min=MathF.Min(r,MathF.Min(g,b)), delta=max-min;
        v=max; s=max>0?delta/max:0;
        if(delta==0) h=0;
        else if(max==r) h=60f*(((g-b)/delta)%6);
        else if(max==g) h=60f*(((b-r)/delta)+2);
        else h=60f*(((r-g)/delta)+4);
        if(h<0) h+=360;
    }

    static Prowl.Vector.Color HSVToColor(float h, float s, float v, float a=1f)
    {
        float c=v*s, x=c*(1f-MathF.Abs((h/60f)%2-1)), m=v-c;
        float r,g,b;
        if(h<60){r=c;g=x;b=0;}else if(h<120){r=x;g=c;b=0;}else if(h<180){r=0;g=c;b=x;}
        else if(h<240){r=0;g=x;b=c;}else if(h<300){r=x;g=0;b=c;}else{r=c;g=0;b=x;}
        return new Prowl.Vector.Color(r+m,g+m,b+m,a);
    }

    static Color32 HSVToColor32(float h, float s, float v)
    { var c=HSVToColor(h,s,v); return Color32.FromArgb(255,(int)(c.R*255),(int)(c.G*255),(int)(c.B*255)); }
}
