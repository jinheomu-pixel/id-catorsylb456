//+------------------------------------------------------------------+
//| IDC Pressure Toolkit - module: inputs and logic                  |
//| Used alone by IDC_Pressure_Toolkit.mq5                           |
//| and with the other tools by IDC_OrderFlow.mq5.                   |
//|                                                                  |
//| Pressure bubbles (dim = weak, bright = strong), big trades       |
//| (outlined square + volume), absorption dots at wicks (red above, |
//| green below, bigger = enhanced), absorption lines extended until |
//| price returns, psychological levels.                             |
//+------------------------------------------------------------------+
#ifndef IDC_PRESSURE_TOOLKIT_MQH
#define IDC_PRESSURE_TOOLKIT_MQH

#include <IDCator/IDCatorCore.mqh>

input group "Pressure Toolkit: General"
input int              InpPtkLookback     = 50;              // Lookback bars (statistics)
input group "Pressure Toolkit: Pressure bubbles"
input bool             InpPtkShowBubbles  = true;            // Show bubbles
input double           InpPtkWeakK        = 1.0;             // Weak bubble (std dev of the heaviest price per bar)
input double           InpPtkStrongK      = 2.0;             // Strong bubble (std dev)
input double           InpPtkDominance    = 1.5;             // Dominance ratio vs opposite side
input int              InpPtkMaxPerSide   = 2;               // Max bubbles per bar and side
input int              InpPtkBubbleMin    = 12;              // Bubble size min
input int              InpPtkBubbleMax    = 24;              // Bubble size max
input int              InpPtkWeakOpacity  = 30;              // Weak bubble opacity %
input int              InpPtkStrongOpacity = 75;             // Strong bubble opacity %
input group "Pressure Toolkit: Big trades"
input bool             InpPtkShowBig      = true;            // Show big trades
input double           InpPtkBigK         = 3.0;             // Big trade (std dev)
input int              InpPtkBigSize      = 11;              // Square size
input group "Pressure Toolkit: Absorption"
input bool             InpPtkShowDots     = true;            // Show absorption dots
input double           InpPtkAbsK         = 1.0;             // Wick level (std dev of volume per price)
input double           InpPtkEnhK         = 2.5;             // Enhanced (std dev)
input double           InpPtkMinWickPct   = 15;              // Min wick share of bar volume %
input bool             InpPtkShowLines    = true;            // Show absorption lines
input bool             InpPtkLiveLines    = true;            // Show the forming bar's lines (live, dashed)
input int              InpPtkLinesPerWick = 8;               // Max lines per wick
input int              InpPtkLineOpacity  = 80;              // Line opacity %
input int              InpPtkMaxLines     = 400;             // Max lines on chart
input group "Pressure Toolkit: Psychological levels"
input bool             InpPtkShowPsych    = true;            // Show psychological levels
input double           InpPtkPsychStep    = 0;               // Step (0 = auto, about 0.3% of price)
input int              InpPtkPsychEach    = 6;               // Levels each side
input int              InpPtkPsychMajor   = 4;               // Major level every
input color            InpPtkPsychColor   = clrGray;         // Level color
input group "Pressure Toolkit: Colors"
input color            InpPtkBuyColor     = C'46,160,90';    // Buy
input color            InpPtkSellColor    = C'214,46,58';    // Sell
input color            InpPtkDotBull      = clrLimeGreen;    // Dot below lower wick
input color            InpPtkDotBear      = C'198,40,40';    // Dot above upper wick
input color            InpPtkTextColor    = C'209,212,220';  // Big trade text
input group "Pressure Toolkit: Alerts"
input bool             InpPtkAlertBubble  = false;           // Strong bubbles
input bool             InpPtkAlertBig     = false;           // Big trades
input bool             InpPtkAlertDot     = false;           // Absorption dots
input bool             InpPtkAlertLine    = false;           // Absorption line retest
input bool             InpPtkAlertPsych   = false;           // Psychological level cross

#define PTK_PFX "IDCptk_"

struct PtkLine
  {
   int               id;
   int               bar;
   int               endBar;    // -1 while active
   double            price;
   int               side;      // aggressor that was absorbed
  };

class CPressureToolkit : public CIdcFeed
  {
public:
   CIdcFootprint     fp;
   CIdcWindowStats   maxStats;  // heaviest aggressive price per bar and side
   CIdcWindowStats   lvlStats;  // total volume per price
   PtkLine           lines[];
   int               nextLine;
   int               liveBar;   // bar whose signal objects are on the chart ("b<bar>_...")
   double            rowSize;
   double            tick;
   double            lastPrice;
   double            barOpen, barHigh, barLow, barClose;

   virtual void      OnReset(void)
     {
      ObjectsDeleteAll(0, PTK_PFX);
      ArrayResize(lines, 0);
      nextLine = 0;
      fp.Clear();
      maxStats.Init(InpPtkLookback * 2);
      lvlStats.Init(InpPtkLookback);
      liveBar = -1;
      lastPrice = 0;
     }

   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live)
     {
      if(fp.Empty())
        {
         barOpen = t.price;
         barHigh = t.price;
         barLow = t.price;
        }
      barHigh = MathMax(barHigh, t.price);
      barLow = MathMin(barLow, t.price);
      barClose = t.price;
      fp.Add(t.price, t.volume, t.side);
      string msg = "";
      // absorption lines: a trade at the line's price (or a gap across it) ends it
      double lo = lastPrice > 0 ? MathMin(lastPrice, t.price) : t.price;
      double hi = lastPrice > 0 ? MathMax(lastPrice, t.price) : t.price;
      for(int i = 0; i < ArraySize(lines); i++)
        {
         if(lines[i].endBar >= 0 || bar <= lines[i].bar)
            continue;
         if(lines[i].price < lo - tick * 0.25 || lines[i].price > hi + tick * 0.25)
            continue;
         lines[i].endBar = bar;
         DrawLine(i, time, rates_total);
         msg += "absorption line retest " + DoubleToString(lines[i].price, _Digits) + "  ";
        }
      // psychological level crossed
      double step = PsychStep(t.price);
      if(lastPrice > 0 && step > 0 && MathFloor(t.price / step) != MathFloor(lastPrice / step))
         if(live && InpPtkAlertPsych)
            msg += "crossed psychological level " + DoubleToString(MathMax(MathFloor(t.price / step), MathFloor(lastPrice / step)) * step, _Digits) + "  ";
      lastPrice = t.price;
      if(live)
        {
         msg += Signals(bar, time, false);
         if(StringLen(msg) > 0 && (InpPtkAlertLine || InpPtkAlertPsych || InpPtkAlertBubble || InpPtkAlertBig || InpPtkAlertDot))
            AlertOnce(bar, msg);
        }
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      Signals(bar, time, true);
      double maxB = 0, maxS = 0;
      for(int i = 0; i < fp.Rows(); i++)
        {
         maxB = MathMax(maxB, fp.buy[i]);
         maxS = MathMax(maxS, fp.sell[i]);
         if(fp.Total(i) > 0)
            lvlStats.Add(fp.Total(i));
        }
      if(maxB > 0)
         maxStats.Add(maxB);
      if(maxS > 0)
         maxStats.Add(maxS);
      maxStats.CloseBar();
      lvlStats.CloseBar();
      fp.Clear();
      liveBar = -1;
      while(ArraySize(lines) > InpPtkMaxLines)
        {
         int drop = 0;
         for(int i = 0; i < ArraySize(lines); i++)
            if(lines[i].endBar >= 0)
              {
               drop = i;
               break;
              }
         ObjectDelete(0, PTK_PFX + "L" + IntegerToString(lines[drop].id));
         ArrayRemove(lines, drop, 1);
        }
     }

   virtual void      OnRefresh(const datetime &time[], const int rates_total)
     {
      for(int i = 0; i < ArraySize(lines); i++)
         if(lines[i].endBar < 0)
            DrawLine(i, time, rates_total);
      if(InpPtkShowPsych && lastPrice > 0)
         DrawPsych();
     }

   //--- bubbles, big trades and dots of a bar; at close also its absorption lines. Returns alert text.
   string            Signals(const int bar, const datetime &time[], const bool closing)
     {
      string pfx = PTK_PFX + "b" + IntegerToString(bar) + "_";
      ObjectsDeleteAll(0, pfx);   // the forming bar is redrawn from scratch
      liveBar = bar;
      string msg = "";
      if(maxStats.Count() < 20 || lvlStats.Count() < 30 || fp.Empty())
         return msg;
      double mMean = maxStats.Mean();
      double weak = maxStats.Threshold(InpPtkWeakK);
      double strong = maxStats.Threshold(InpPtkStrongK);
      double big = MathMax(maxStats.Threshold(InpPtkBigK), weak);
      datetime t = time[bar] + PeriodSeconds() / 2;
      int n = fp.Rows();
      // bubbles: the heaviest qualifying prices per side
      for(int side = 1; side >= -1; side -= 2)
        {
         bool used[];
         ArrayResize(used, n);
         for(int i = 0; i < n; i++)
            used[i] = false;
         for(int k = 0; k < InpPtkMaxPerSide; k++)
           {
            int best = -1;
            for(int i = 0; i < n; i++)
              {
               double v = side > 0 ? fp.buy[i] : fp.sell[i];
               double o = side > 0 ? fp.sell[i] : fp.buy[i];
               if(used[i] || v < weak || v <= mMean || v <= o || v < InpPtkDominance * o)
                  continue;
               if(best < 0 || v > (side > 0 ? fp.buy[best] : fp.sell[best]))
                  best = i;
              }
            if(best < 0)
               break;
            used[best] = true;
            double v = side > 0 ? fp.buy[best] : fp.sell[best];
            double px = fp.RowLow(best) + (rowSize - tick) / 2;
            bool isStrong = v >= strong;
            color base = side > 0 ? InpPtkBuyColor : InpPtkSellColor;
            string id = pfx + (side > 0 ? "B" : "S") + IntegerToString(k);
            if(InpPtkShowBubbles)
              {
               double f = MathSqrt(MathMax(0.0, MathMin(1.0, (v / MathMax(weak, 1e-9) - 1) / 3)));
               int size = (int)MathRound(InpPtkBubbleMin + (InpPtkBubbleMax - InpPtkBubbleMin) * f);
               IdcText(id + "c", t, px, IdcGlyph(IDC_WD_CIRCLE), IdcBlend(base, (isStrong ? InpPtkStrongOpacity : InpPtkWeakOpacity) / 100.0),
                       size, "Wingdings", ANCHOR_CENTER, false);
              }
            if(isStrong && InpPtkAlertBubble)
               msg += (side > 0 ? "strong buy bubble " : "strong sell bubble ") + DoubleToString(px, _Digits) + "  ";
            if(InpPtkShowBig && k == 0 && v >= big)
              {
               IdcText(id + "q", t, px, IdcGlyph(IDC_WD_SQUARE), side > 0 ? IdcRgb(120, 230, 120) : IdcRgb(240, 110, 100), InpPtkBigSize,
                       "Wingdings", ANCHOR_CENTER, false);
               IdcText(id + "v", t, px, IdcVolText(v, false), InpPtkTextColor, 8, "Arial", side > 0 ? ANCHOR_UPPER : ANCHOR_LOWER, false);
               if(InpPtkAlertBig)
                  msg += "big " + (side > 0 ? "buy " : "sell ") + IdcVolText(v, false) + " @ " + DoubleToString(px, _Digits) + "  ";
              }
           }
        }
      // absorption: heavy volume traded in a wick (rows outside the body)
      double total = fp.TotalVolume();
      double lMean = lvlStats.Mean();
      double wickThr = lvlStats.Threshold(InpPtkAbsK);
      double enhThr = lvlStats.Threshold(InpPtkEnhK);
      double bodyTop = MathMax(barOpen, barClose), bodyBot = MathMin(barOpen, barClose);
      for(int w = 0; w < 2; w++)
        {
         bool atHigh = w == 0;
         double wickVol = 0, maxLevel = 0;
         for(int i = 0; i < n; i++)
           {
            double lowP = fp.RowLow(i), highP = fp.RowLow(i) + rowSize - tick;
            bool inWick = atHigh ? lowP > bodyTop + tick * 0.5 : highP < bodyBot - tick * 0.5;
            if(!inWick)
               continue;
            wickVol += fp.Total(i);
            maxLevel = MathMax(maxLevel, fp.Total(i));
           }
         if(!(maxLevel >= wickThr && maxLevel > lMean && total > 0 && wickVol / total * 100.0 >= InpPtkMinWickPct))
            continue;
         bool enhanced = maxLevel >= enhThr;
         if(InpPtkShowDots)
           {
            double y = atHigh ? barHigh + 3 * tick : barLow - 3 * tick;
            IdcText(pfx + (atHigh ? "dh" : "dl"), t, y, IdcGlyph(IDC_WD_CIRCLE), atHigh ? InpPtkDotBear : InpPtkDotBull, enhanced ? 9 : 6,
                    "Wingdings", atHigh ? ANCHOR_LOWER : ANCHOR_UPPER, false);
           }
         if(InpPtkAlertDot)
            msg += (enhanced ? "enhanced " : "") + "absorption dot " + (atHigh ? "above " : "below ") + "  ";
         if(InpPtkShowLines && (closing || InpPtkLiveLines))
            AddLines(bar, atHigh, bodyTop, bodyBot, wickThr, time, !closing, pfx);
        }
      return msg;
     }

   //--- absorption lines from the heaviest wick prices: kept at the close; live (forming bar): dashed, under the bar's
   //--- prefix, so the next redraw of the bar replaces them
   void              AddLines(const int bar, const bool atHigh, const double bodyTop, const double bodyBot, const double thr, const datetime &time[],
                              const bool live, const string pfx)
     {
      int n = fp.Rows();
      bool used[];
      ArrayResize(used, n);
      for(int i = 0; i < n; i++)
         used[i] = false;
      for(int k = 0; k < InpPtkLinesPerWick; k++)
        {
         int best = -1;
         for(int i = 0; i < n; i++)
           {
            double lowP = fp.RowLow(i), highP = fp.RowLow(i) + rowSize - tick;
            bool inWick = atHigh ? lowP > bodyTop + tick * 0.5 : highP < bodyBot - tick * 0.5;
            if(!inWick || used[i] || fp.Total(i) < thr)
               continue;
            if(best < 0 || fp.Total(i) > fp.Total(best))
               best = i;
           }
         if(best < 0)
            break;
         used[best] = true;
         double price = fp.RowLow(best) + (rowSize - tick) / 2;
         if(live)
           {
            IdcLine(pfx + "L" + (atHigh ? "h" : "l") + IntegerToString(k), time[bar] + PeriodSeconds() / 2, price, time[bar] + PeriodSeconds(), price,
                    IdcBlend(atHigh ? InpPtkBuyColor : InpPtkSellColor, InpPtkLineOpacity / 100.0), 1, STYLE_DASH, false);
            continue;
           }
         int j = ArraySize(lines);
         ArrayResize(lines, j + 1);
         lines[j].id = nextLine++;
         lines[j].bar = bar;
         lines[j].endBar = -1;
         lines[j].price = price;
         lines[j].side = atHigh ? 1 : -1;
        }
     }

   void              DrawLine(const int i, const datetime &time[], const int rates_total)
     {
      int last = lines[i].endBar >= 0 ? lines[i].endBar : rates_total - 1;
      if(last >= rates_total)
         last = rates_total - 1;
      color base = lines[i].side > 0 ? InpPtkBuyColor : InpPtkSellColor;
      IdcLine(PTK_PFX + "L" + IntegerToString(lines[i].id), time[lines[i].bar] + PeriodSeconds() / 2, lines[i].price,
              time[last] + PeriodSeconds(), lines[i].price, IdcBlend(base, InpPtkLineOpacity / 100.0), 1, STYLE_SOLID, false);
     }

   double            PsychStep(const double price)
     {
      if(InpPtkPsychStep > 0)
         return InpPtkPsychStep;
      if(price <= 0)
         return 0;
      double target = price * 0.003;
      double mag = MathPow(10, MathFloor(MathLog10(target)));
      double nice[7] = {1, 2.5, 5, 10, 25, 50, 100};
      for(int i = 0; i < ArraySize(nice); i++)
        {
         double st = mag * nice[i];
         double ticks = st / tick;
         if(st >= target * 0.75 && ticks >= 1 - 1e-9 && MathAbs(ticks - MathRound(ticks)) < 1e-6)
            return MathRound(ticks) * tick;
        }
      return MathMax(1, MathCeil(target / tick - 1e-9)) * tick;
     }

   void              DrawPsych(void)
     {
      double step = PsychStep(lastPrice);
      if(step <= 0)
         return;
      int base = (int)MathFloor(lastPrice / step);
      for(int k = -InpPtkPsychEach + 1; k <= InpPtkPsychEach; k++)
        {
         int lvl = base + k;
         string name = PTK_PFX + "P" + IntegerToString(k + InpPtkPsychEach);
         if(ObjectFind(0, name) < 0)
           {
            ObjectCreate(0, name, OBJ_HLINE, 0, 0, lvl * step);
            ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
            ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
            ObjectSetInteger(0, name, OBJPROP_BACK, true);
           }
         bool major = (lvl % MathMax(1, InpPtkPsychMajor)) == 0;
         ObjectSetDouble(0, name, OBJPROP_PRICE, 0, lvl * step);
         ObjectSetInteger(0, name, OBJPROP_COLOR, IdcBlend(InpPtkPsychColor, 0.6));
         ObjectSetInteger(0, name, OBJPROP_WIDTH, major ? 2 : 1);
         ObjectSetInteger(0, name, OBJPROP_STYLE, major ? STYLE_SOLID : STYLE_DOT);
        }
     }

   int               alertBar;
   string            alertSent;
   void              AlertOnce(const int bar, const string msg)
     {
      // one alert per bar and message (realtime ticks re-evaluate the forming bar)
      if(bar == alertBar && StringFind(alertSent, msg) >= 0)
         return;
      if(bar != alertBar)
        {
         alertBar = bar;
         alertSent = "";
        }
      alertSent += msg;
      Alert(_Symbol + " " + msg);
     }

   //--- symbol tick and the initial row size (rowPoints 0: one tick until SetRow)
   void              Init(const int rowPoints)
     {
      tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      if(tick <= 0)
         tick = _Point;
      SetRow(IdcRowSize(_Symbol, rowPoints));
      nextLine = 0;
      liveBar = -1;
      alertBar = -1;
      alertSent = "";
     }

   //--- footprint row size (automatic: IdcAutoRow from the chart's bars)
   void              SetRow(const double row)
     {
      rowSize = row;
      fp.rowSize = row;
     }
  };

#endif
