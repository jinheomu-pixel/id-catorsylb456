//+------------------------------------------------------------------+
//| IDC Imbalance Levels - module: inputs and logic                  |
//| Used alone by IDC_Imbalance_Levels.mq5                           |
//| and with the other tools by IDC_OrderFlow.mq5.                   |
//|                                                                  |
//| Stacked diagonal imbalances of every closed bar become support   |
//| (buy stack below the close) and resistance (sell stack above the |
//| close) zones, extended until price trades back into them.        |
//| Label: strongest ratio | stack volume. Darker = stronger.        |
//+------------------------------------------------------------------+
#ifndef IDC_IMBALANCE_LEVELS_MQH
#define IDC_IMBALANCE_LEVELS_MQH

#include <IDCator/IDCatorCore.mqh>

enum ENUM_IDC_ZERO
  {
   IDC_ZERO_MINRATIO = 0, // Count as min ratio
   IDC_ZERO_ONE      = 1, // Count as 1
   IDC_ZERO_SKIP     = 2  // Skip
  };

input group "Imbalance: Detection"
input double           InpImbMinRatio     = 5.0;             // Min imbalance ratio
input int              InpImbMinStack     = 3;               // Min stacked levels
input double           InpImbMinLevelVol  = 0;               // Min volume per level
input double           InpImbMinStackVol  = 0;               // Min stack volume
input ENUM_IDC_ZERO    InpImbZero         = IDC_ZERO_MINRATIO; // Zero-value handling
input bool             InpImbBeyondClose  = true;            // Buy stacks below close, sell stacks above
input bool             InpImbDiscardNext  = false;           // Discard if next bar touches
input group "Imbalance: Display"
input color            InpImbSupColor     = C'34,171,92';    // Support (buy stack)
input color            InpImbResColor     = C'233,57,61';    // Resistance (sell stack)
input int              InpImbMinOpacity   = 10;              // Opacity at min ratio %
input int              InpImbMaxOpacity   = 45;              // Opacity at full ratio %
input double           InpImbFullRatio    = 15;              // Full-opacity ratio
input bool             InpImbShowLabels   = true;            // Show labels
input color            InpImbTextColor    = C'224,224,224';  // Label color
input int              InpImbTextSize     = 8;               // Label size
input bool             InpImbKeepTouched  = true;            // Keep touched zones
input bool             InpImbShowLive     = true;            // Show the forming bar's stacks (live, dashed)
input int              InpImbMaxZones     = 300;             // Max zones on chart
input group "Imbalance: Alerts"
input bool             InpImbAlertTouch   = true;            // Alert when price touches a zone

#define IMB_PFX "IDCimb_"

struct ImbStack
  {
   bool              support;
   double            bottom;
   double            top;
   double            ratio;
   double            volume;
  };

struct ImbZone
  {
   int               id;
   int               startBar;
   int               endBar;     // -1 while active
   bool              support;
   double            bottom;     // row low of the lowest level
   double            top;        // row high of the highest level
   double            ratio;
   double            volume;
  };

class CImbalance : public CIdcFeed
  {
public:
   CIdcFootprint     fp;
   ImbZone           zones[];
   int               nextId;
   double            rowSize;
   double            tick;
   double            lastPrice;   // last trade: the forming bar's close so far
   int               liveBar;     // the forming bar (-1 none)

   virtual void      OnReset(void)
     {
      ObjectsDeleteAll(0, IMB_PFX);
      ArrayResize(zones, 0);
      nextId = 0;
      fp.Clear();
     }

   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live)
     {
      fp.Add(t.price, t.volume, t.side);
      lastPrice = t.price;
      liveBar = bar;
      // a trade inside an active zone touches it on that tick
      string msg = "";
      for(int i = ArraySize(zones) - 1; i >= 0; i--)
        {
         if(zones[i].endBar >= 0 || bar <= zones[i].startBar)
            continue;
         // the zone spans its rows: from the lowest row's low up to the highest row's last tick
         if(t.price < zones[i].bottom - tick * 0.25 || t.price > zones[i].top - tick * 0.75)
            continue;
         if(InpImbDiscardNext && bar == zones[i].startBar + 1)
           {
            DeleteZone(i);
            continue;
           }
         zones[i].endBar = bar;
         if(!InpImbKeepTouched)
           {
            msg += ZoneText(zones[i]) + "  ";
            DeleteZone(i);
            continue;
           }
         Draw(i, time, rates_total);
         msg += (zones[i].support ? "support " : "resistance ") + DoubleToString(zones[i].bottom, _Digits) + "-" +
                DoubleToString(zones[i].top - tick, _Digits) + " (" + ZoneText(zones[i]) + ")  ";
        }
      if(live && InpImbAlertTouch && StringLen(msg) > 0)
         Alert(_Symbol + " touched imbalance " + msg);
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      ClearLive();
      if(fp.Empty())
         return;
      double close = iClose(_Symbol, _Period, iBarShift(_Symbol, _Period, time[bar]));
      ImbStack st[];
      int n = Stacks(close, st);
      for(int i = 0; i < n; i++)
         AddZone(bar, st[i].support, st[i].bottom, st[i].top, st[i].ratio, st[i].volume, time, rates_total);
      fp.Clear();
     }

   virtual void      OnRefresh(const datetime &time[], const int rates_total)
     {
      for(int i = 0; i < ArraySize(zones); i++)
         if(zones[i].endBar < 0)
            Draw(i, time, rates_total);
      if(InpImbShowLive)
         DrawLive(time, rates_total);
     }

   //--- stacked diagonal imbalances of the forming bar's footprint; with the close filter, buy stacks below `close`
   //--- and sell stacks above it
   int               Stacks(const double close, ImbStack &out[])
     {
      ArrayResize(out, 0);
      int n = fp.Rows();
      // buy stacks: row i buy vs row i-1 sell (diagonal)
      int runStart = -1;
      double runMax = 0, runVol = 0;
      for(int i = 1; i <= n; i++)
        {
         double r = i < n ? LevelRatio(fp.buy[i], fp.sell[i - 1]) : -1;
         if(r >= InpImbMinRatio * (1 - 1e-9))   // fractional (crypto) volumes: a ratio of exactly 5 may sum to 4.9999999
           {
            if(runStart < 0)
              {
               runStart = i;
               runMax = r;
               runVol = 0;
              }
            runMax = MathMax(runMax, r);
            runVol += fp.buy[i];
           }
         else
            if(runStart >= 0)
              {
               int runEnd = i - 1;
               double bottom = fp.RowLow(runStart), top = fp.RowLow(runEnd) + rowSize;
               bool okSide = !InpImbBeyondClose || top - tick * 0.5 < close;   // the whole stack below the close
               if(runEnd - runStart + 1 >= InpImbMinStack && runVol >= InpImbMinStackVol && okSide)
                  PushStack(out, true, bottom, top, runMax, runVol);
               runStart = -1;
              }
        }
      // sell stacks: row i sell vs row i+1 buy
      runStart = -1;
      for(int i = 0; i < n; i++)
        {
         double r = i < n - 1 ? LevelRatio(fp.sell[i], fp.buy[i + 1]) : -1;
         if(r >= InpImbMinRatio * (1 - 1e-9))
           {
            if(runStart < 0)
              {
               runStart = i;
               runMax = r;
               runVol = 0;
              }
            runMax = MathMax(runMax, r);
            runVol += fp.sell[i];
           }
         else
            if(runStart >= 0)
              {
               int runEnd = i - 1;
               double bottom = fp.RowLow(runStart), top = fp.RowLow(runEnd) + rowSize;
               bool okSide = !InpImbBeyondClose || bottom > close + tick * 0.5;
               if(runEnd - runStart + 1 >= InpImbMinStack && runVol >= InpImbMinStackVol && okSide)
                  PushStack(out, false, bottom, top, runMax, runVol);
               runStart = -1;
              }
        }
      return ArraySize(out);
     }

   void              PushStack(ImbStack &out[], const bool support, const double bottom, const double top, const double ratio, const double volume)
     {
      int k = ArraySize(out);
      ArrayResize(out, k + 1);
      out[k].support = support;
      out[k].bottom = bottom;
      out[k].top = top;
      out[k].ratio = ratio;
      out[k].volume = volume;
     }

   //--- the forming bar's stacks so far: filled box + dashed frame + label, redrawn on every refresh until the close
   void              DrawLive(const datetime &time[], const int rates_total)
     {
      ClearLive();
      if(fp.Empty() || liveBar < 0 || liveBar >= rates_total)
         return;
      ImbStack st[];
      int n = Stacks(lastPrice, st);
      datetime t1 = time[liveBar], t2 = time[liveBar] + PeriodSeconds();
      for(int i = 0; i < n; i++)
        {
         string id = IMB_PFX + "L" + IntegerToString(i);
         color base = st[i].support ? InpImbSupColor : InpImbResColor;
         double span = MathMax(0.0001, InpImbFullRatio - InpImbMinRatio);
         double f = MathMax(0.0, MathMin(1.0, (st[i].ratio - InpImbMinRatio) / span));
         IdcRect(id + "z", t1, st[i].top, t2, st[i].bottom, IdcBlend(base, (InpImbMinOpacity + (InpImbMaxOpacity - InpImbMinOpacity) * f) / 100.0));
         IdcFrame(id + "f", t1, st[i].top, t2, st[i].bottom, IdcBlend(base, 0.8));
         if(InpImbShowLabels)
            IdcText(id + "t", t2, (st[i].top + st[i].bottom) / 2, IdcRatioText(st[i].ratio) + " | " + IdcVolText(st[i].volume, false),
                    InpImbTextColor, InpImbTextSize, "Arial Bold", ANCHOR_RIGHT, false);
        }
     }

   void              ClearLive(void) { ObjectsDeleteAll(0, IMB_PFX + "L"); }

   //--- ratio of an aggressive level against its diagonal opposite, -1 when the level does not count
   double            LevelRatio(const double aggressive, const double opposite)
     {
      if(aggressive <= 0 || aggressive < InpImbMinLevelVol)
         return -1;
      if(opposite > 0)
         return aggressive / opposite;
      if(InpImbZero == IDC_ZERO_MINRATIO)
         return InpImbMinRatio;
      if(InpImbZero == IDC_ZERO_ONE)
         return aggressive;
      return -1;
     }

   string            ZoneText(const ImbZone &z) { return IdcRatioText(z.ratio) + " | " + IdcVolText(z.volume, false); }

   void              AddZone(const int bar, const bool support, const double bottom, const double top, const double ratio,
                             const double volume, const datetime &time[], const int rates_total)
     {
      int k = ArraySize(zones);
      ArrayResize(zones, k + 1);
      zones[k].id = nextId++;
      zones[k].startBar = bar;
      zones[k].endBar = -1;
      zones[k].support = support;
      zones[k].bottom = bottom;
      zones[k].top = top;
      zones[k].ratio = ratio;
      zones[k].volume = volume;
      Draw(k, time, rates_total);
      if(ArraySize(zones) > InpImbMaxZones)
        {
         // drop the oldest touched zone first, then the oldest one
         int drop = 0;
         for(int i = 0; i < ArraySize(zones); i++)
            if(zones[i].endBar >= 0)
              {
               drop = i;
               break;
              }
         DeleteZone(drop);
        }
     }

   void              DeleteZone(const int i)
     {
      ObjectDelete(0, IMB_PFX + "z" + IntegerToString(zones[i].id));
      ObjectDelete(0, IMB_PFX + "t" + IntegerToString(zones[i].id));
      ArrayRemove(zones, i, 1);
     }

   void              Draw(const int i, const datetime &time[], const int rates_total)
     {
      int last = zones[i].endBar >= 0 ? zones[i].endBar : rates_total - 1;
      if(last >= rates_total)
         last = rates_total - 1;
      datetime t1 = time[zones[i].startBar];
      datetime t2 = time[last] + PeriodSeconds();
      double span = MathMax(0.0001, InpImbFullRatio - InpImbMinRatio);
      double f = MathMax(0.0, MathMin(1.0, (zones[i].ratio - InpImbMinRatio) / span));
      double opacity = (InpImbMinOpacity + (InpImbMaxOpacity - InpImbMinOpacity) * f) / 100.0;
      color base = zones[i].support ? InpImbSupColor : InpImbResColor;
      string id = IntegerToString(zones[i].id);
      IdcRect(IMB_PFX + "z" + id, t1, zones[i].top, t2, zones[i].bottom, IdcBlend(base, opacity));
      if(InpImbShowLabels)
         IdcText(IMB_PFX + "t" + id, t2, (zones[i].top + zones[i].bottom) / 2, ZoneText(zones[i]), InpImbTextColor, InpImbTextSize,
                 "Arial Bold", ANCHOR_RIGHT, false);
     }

   //--- symbol tick and the initial row size (rowPoints 0: one tick until SetRow)
   void              Init(const int rowPoints)
     {
      tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      if(tick <= 0)
         tick = _Point;
      SetRow(IdcRowSize(_Symbol, rowPoints));
      nextId = 0;
      lastPrice = 0;
      liveBar = -1;
     }

   //--- footprint row size (automatic: IdcAutoRow from the chart's bars)
   void              SetRow(const double row)
     {
      rowSize = row;
      fp.rowSize = row;
     }
  };

#endif
