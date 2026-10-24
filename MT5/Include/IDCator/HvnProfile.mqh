//+------------------------------------------------------------------+
//| IDC HVN Profile - module: inputs and logic                       |
//| Used alone by IDC_HVN_Profile.mq5                                |
//| and with the other tools by IDC_OrderFlow.mq5.                   |
//|                                                                  |
//| Volume profile per period (default 5 minutes); its High Volume   |
//| Nodes (smoothed profile >= threshold % of the peak, clustered)   |
//| become zones from the period start. Biggest node orange, others  |
//| slate blue. Label "N | P%": N = volume at the node's POC, P =    |
//| share of the dominant side (green buyers / red sellers).         |
//| Zones extend until price trades back into them.                  |
//+------------------------------------------------------------------+
#ifndef IDC_HVN_PROFILE_MQH
#define IDC_HVN_PROFILE_MQH

#include <IDCator/IDCatorCore.mqh>

input group "HVN: Profile"
input ENUM_TIMEFRAMES  InpHvnPeriod       = PERIOD_M5;       // Profile timeframe
input int              InpHvnRowPoints    = 0;               // Row size in points (0 = auto)
input int              InpHvnBasePoints   = 0;               // Footprint row in points (0 = auto)
input int              InpHvnTargetRows   = 60;              // Auto: rows per profile
input group "HVN: High volume nodes"
input double           InpHvnThreshold    = 70;              // HVN threshold %
input int              InpHvnSmoothing    = 2;               // Smoothing passes
input double           InpHvnSensitivity  = 25;              // Cluster sensitivity %
input int              InpHvnMinRows      = 2;               // Min cluster size (rows)
input int              InpHvnGap          = 1;               // Gap tolerance (rows)
input int              InpHvnMaxClusters  = 3;               // Max clusters per period
input int              InpHvnGraceBars    = 0;               // Touch grace bars
input group "HVN: Display"
input color            InpHvnPrimary      = C'255,165,0';    // Primary zone
input color            InpHvnSecondary    = C'110,125,160';  // Other zones
input int              InpHvnOpacity      = 22;              // Zone opacity %
input bool             InpHvnKeepTouched  = true;            // Keep touched zones
input bool             InpHvnShowLive     = true;            // Show the forming period's nodes (live, dashed)
input bool             InpHvnShowLabels   = true;            // Show labels
input color            InpHvnLabelColor   = clrWhite;        // Label number
input color            InpHvnBuyPct       = C'46,160,110';   // Buyers %
input color            InpHvnSellPct      = C'192,74,68';    // Sellers %
input int              InpHvnLabelSize    = 8;               // Label size
input bool             InpHvnLabelPoc     = true;            // Label value: POC volume (false = cluster volume)
input bool             InpHvnShowPoc      = false;           // Show POC line
input color            InpHvnPocColor     = C'218,165,32';   // POC color
input int              InpHvnMaxZones     = 300;             // Max zones on chart
input group "HVN: Alerts"
input bool             InpHvnAlertTouch   = true;            // Alert when price touches a zone

#define HVN_PFX "IDChvn_"

struct HvnCluster
  {
   double            bottom;
   double            top;
   double            poc;
   double            label;
   double            buyShare;
  };

struct HvnZone
  {
   int               id;
   int               startBar;
   int               periodEndBar;
   int               endBar;      // -1 while active
   bool              primary;
   double            bottom;
   double            top;
   double            poc;
   double            label;
   double            buyShare;    // 0..1
  };

class CHvnProfile : public CIdcFeed
  {
public:
   CIdcFootprint     bar_;        // forming bar (base rows)
   CIdcFootprint     per;         // current period (base rows)
   datetime          perKey;      // start time of the current period (0 none)
   int               perStart;
   int               perEnd;
   HvnZone           zones[];
   int               nextId;
   double            tick;
   int               liveBar;     // the forming bar (-1 none)

   virtual void      OnReset(void)
     {
      ObjectsDeleteAll(0, HVN_PFX);
      ArrayResize(zones, 0);
      nextId = 0;
      bar_.Clear();
      per.Clear();
      perKey = 0;
      perStart = -1;
      perEnd = -1;
     }

   datetime          KeyOf(const datetime t)
     {
      int shift = iBarShift(_Symbol, InpHvnPeriod, t, false);
      if(shift < 0)
         return (datetime)((long)t / PeriodSeconds(InpHvnPeriod) * PeriodSeconds(InpHvnPeriod));
      return iTime(_Symbol, InpHvnPeriod, shift);
     }

   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live)
     {
      bar_.Add(t.price, t.volume, t.side);
      liveBar = bar;
      string msg = "";
      for(int i = ArraySize(zones) - 1; i >= 0; i--)
        {
         if(zones[i].endBar >= 0 || bar <= zones[i].periodEndBar + InpHvnGraceBars)
            continue;
         if(t.price < zones[i].bottom - tick * 0.25 || t.price > zones[i].top - tick * 0.75)
            continue;
         zones[i].endBar = bar;
         msg += DoubleToString(zones[i].bottom, _Digits) + "-" + DoubleToString(zones[i].top - tick, _Digits) + "  ";
         if(!InpHvnKeepTouched)
            DeleteZone(i);
         else
            Draw(i, time, rates_total);
        }
      if(live && InpHvnAlertTouch && StringLen(msg) > 0)
         Alert(_Symbol + " touched HVN zone " + msg);
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      datetime key = KeyOf(time[bar]);
      if(perKey != 0 && key != perKey)
         Finalize(time, rates_total);
      if(perKey == 0)
        {
         perKey = key;
         perStart = bar;
         per.Clear();
        }
      for(int i = 0; i < bar_.Rows(); i++)
        {
         double p = bar_.RowLow(i) + bar_.rowSize * 0.5;
         if(bar_.buy[i] > 0)
            per.Add(p, bar_.buy[i], 1);
         if(bar_.sell[i] > 0)
            per.Add(p, bar_.sell[i], -1);
        }
      perEnd = bar;
      bar_.Clear();
      // the period ends with this bar: close it now instead of waiting for the next period's first trade
      if(time[bar] + PeriodSeconds() >= perKey + PeriodSeconds(InpHvnPeriod) && PeriodSeconds(InpHvnPeriod) < 86400)
         Finalize(time, rates_total);
     }

   virtual void      OnRefresh(const datetime &time[], const int rates_total)
     {
      for(int i = 0; i < ArraySize(zones); i++)
         if(zones[i].endBar < 0)
            Draw(i, time, rates_total);
      if(InpHvnShowLive)
         DrawLive(time, rates_total);
     }

   void              Finalize(const datetime &time[], const int rates_total)
     {
      if(per.Rows() > 0)
         Analyze(time, rates_total);
      per.Clear();
      perKey = 0;
      perStart = -1;
     }

   //--- HVN clusters of a footprint (base rows): profile rows, smoothing, clusters, largest first
   int               Clusters(const CIdcFootprint &src, HvnCluster &out[])
     {
      ArrayResize(out, 0);
      int rows = src.Rows();
      // profile row: a size in ticks rounded up to whole base rows (as in the TradingView version), rt = base rows per row
      int baseTicks = (int)MathMax(1, MathRound(src.rowSize / tick));
      int rawTicks = InpHvnRowPoints > 0 ? (int)MathMax(1, MathRound(InpHvnRowPoints * _Point / tick)) :
                     (int)MathMax(1, MathCeil(((rows - 1) * baseTicks + 1) / (double)MathMax(5, InpHvnTargetRows)));
      int rt = (int)MathCeil(rawTicks / (double)baseTicks);
      int n = (rows + rt - 1) / rt;
      double vol[], buy[], sell[], sm[], nxt[];
      ArrayResize(vol, n);
      ArrayResize(buy, n);
      ArrayResize(sell, n);
      ArrayInitialize(vol, 0);
      ArrayInitialize(buy, 0);
      ArrayInitialize(sell, 0);
      for(int k = 0; k < rows; k++)
        {
         int r = k / rt;
         vol[r] += src.buy[k] + src.sell[k];
         buy[r] += src.buy[k];
         sell[r] += src.sell[k];
        }
      ArrayResize(sm, n);
      ArrayCopy(sm, vol);
      for(int pass = 0; pass < InpHvnSmoothing; pass++)
        {
         ArrayResize(nxt, n);
         for(int r = 0; r < n; r++)
           {
            double s = 2 * sm[r], w = 2;
            if(r > 0)
              {
               s += sm[r - 1];
               w += 1;
              }
            if(r < n - 1)
              {
               s += sm[r + 1];
               w += 1;
              }
            nxt[r] = s / w;
           }
         ArrayCopy(sm, nxt);
        }
      double mx = sm[ArrayMaximum(sm)];
      if(mx <= 0)
         return 0;
      double thr = mx * InpHvnThreshold / 100.0;
      // candidate clusters
      int cFirst[], cLast[];
      double cVol[];
      int row = 0;
      while(row < n)
        {
         if(sm[row] < thr)
           {
            row++;
            continue;
           }
         int first = row, last = row, gap = 0;
         for(int r = row + 1; r < n; r++)
           {
            if(sm[r] >= thr)
              {
               last = r;
               gap = 0;
              }
            else
              {
               gap++;
               if(gap > InpHvnGap)
                  break;
              }
           }
         row = last + 1;
         double peak = 0;
         for(int r = first; r <= last; r++)
            peak = MathMax(peak, sm[r]);
         double edge = thr + (peak - thr) * InpHvnSensitivity / 100.0;
         int uFirst = first, uLast = last;
         while(first < last && sm[first] < edge)
            first++;
         while(last > first && sm[last] < edge)
            last--;
         while(last - first + 1 < InpHvnMinRows && (first > uFirst || last < uLast))
           {
            if(first > uFirst && (last >= uLast || sm[first - 1] >= sm[last + 1]))
               first--;
            else
               last++;
           }
         if(last - first + 1 < InpHvnMinRows)
            continue;
         int c = ArraySize(cFirst);
         ArrayResize(cFirst, c + 1);
         ArrayResize(cLast, c + 1);
         ArrayResize(cVol, c + 1);
         cFirst[c] = first;
         cLast[c] = last;
         cVol[c] = 0;
         for(int r = first; r <= last; r++)
            cVol[c] += vol[r];
        }
      // largest first, at most InpHvnMaxClusters
      int count = ArraySize(cFirst);
      bool used[];
      ArrayResize(used, count);
      for(int c = 0; c < count; c++)
         used[c] = false;
      for(int rank = 0; rank < MathMin(count, InpHvnMaxClusters); rank++)
        {
         int best = -1;
         for(int c = 0; c < count; c++)
            if(!used[c] && (best < 0 || cVol[c] > cVol[best]))
               best = c;
         if(best < 0)
            break;
         used[best] = true;
         double b = 0, s = 0, pocVol = 0;
         int pocTick = cFirst[best] * rt;
         for(int r = cFirst[best]; r <= cLast[best]; r++)
           {
            b += buy[r];
            s += sell[r];
           }
         for(int k = cFirst[best] * rt; k < MathMin(rows, (cLast[best] + 1) * rt); k++)
            if(src.buy[k] + src.sell[k] > pocVol)
              {
               pocVol = src.buy[k] + src.sell[k];
               pocTick = k;
              }
         int k = ArraySize(out);
         ArrayResize(out, k + 1);
         out[k].bottom = src.RowLow(cFirst[best] * rt);
         out[k].top = src.RowLow(MathMin(rows - 1, (cLast[best] + 1) * rt - 1)) + src.rowSize;
         out[k].poc = src.RowLow(pocTick);
         out[k].label = InpHvnLabelPoc ? pocVol : cVol[best];
         out[k].buyShare = b + s > 0 ? b / (b + s) : 0.5;
        }
      return ArraySize(out);
     }

   //--- the finished period's clusters become zones
   void              Analyze(const datetime &time[], const int rates_total)
     {
      HvnCluster cl[];
      int n = Clusters(per, cl);
      for(int r = 0; r < n; r++)
        {
         int z = ArraySize(zones);
         ArrayResize(zones, z + 1);
         zones[z].id = nextId++;
         zones[z].startBar = perStart;
         zones[z].periodEndBar = perEnd;
         zones[z].endBar = -1;
         zones[z].primary = r == 0;
         zones[z].bottom = cl[r].bottom;
         zones[z].top = cl[r].top;
         zones[z].poc = cl[r].poc;
         zones[z].label = cl[r].label;
         zones[z].buyShare = cl[r].buyShare;
         Draw(z, time, rates_total);
        }
      while(ArraySize(zones) > InpHvnMaxZones)
        {
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

   //--- the forming period's nodes so far: dashed frames, redrawn on every refresh until the period closes
   void              DrawLive(const datetime &time[], const int rates_total)
     {
      ObjectsDeleteAll(0, HVN_PFX + "L");
      if(liveBar < 0 || liveBar >= rates_total)
         return;
      CIdcFootprint tmp;
      tmp.rowSize = per.rowSize;
      int start = liveBar;
      // `per` holds the forming period unless that one already ended (daily periods close on the next period's bar)
      if(perKey != 0 && perStart >= 0 && perKey == KeyOf(time[liveBar]))
        {
         tmp.CopyFrom(per);
         start = perStart;
        }
      for(int i = 0; i < bar_.Rows(); i++)
        {
         double p = bar_.RowLow(i) + bar_.rowSize * 0.5;
         if(bar_.buy[i] > 0)
            tmp.Add(p, bar_.buy[i], 1);
         if(bar_.sell[i] > 0)
            tmp.Add(p, bar_.sell[i], -1);
        }
      if(tmp.Empty())
         return;
      HvnCluster cl[];
      int n = Clusters(tmp, cl);
      datetime t1 = time[start], t2 = time[rates_total - 1] + PeriodSeconds();
      for(int r = 0; r < n; r++)
        {
         string id = HVN_PFX + "L" + IntegerToString(r);
         color base = r == 0 ? InpHvnPrimary : InpHvnSecondary;
         IdcRect(id + "z", t1, cl[r].top, t2, cl[r].bottom, IdcBlend(base, InpHvnOpacity / 100.0));
         IdcFrame(id + "f", t1, cl[r].top, t2, cl[r].bottom, IdcBlend(base, 0.75));
         if(InpHvnShowLabels)
            NodeLabels(id + "n", id + "p", t2, (cl[r].top + cl[r].bottom) / 2, cl[r].label, cl[r].buyShare);
        }
     }

   //--- the two labels of a node: the number in white, " | P%" in the dominant side's colour (same monospace font)
   void              NodeLabels(const string nName, const string pName, const datetime t2, const double mid, const double label,
                                const double buyShare)
     {
      bool buyers = buyShare >= 0.5;
      int pct = (int)MathMax(51, MathRound(100 * (buyers ? buyShare : 1 - buyShare)));
      string pctText = " | " + IntegerToString(pct) + "%";
      string num = IdcVolText(label, false);
      for(int k = 0; k < StringLen(pctText); k++)
         num += " ";
      IdcText(nName, t2, mid, num, InpHvnLabelColor, InpHvnLabelSize, "Courier New Bold", ANCHOR_RIGHT, false);
      IdcText(pName, t2, mid, pctText, buyers ? InpHvnBuyPct : InpHvnSellPct, InpHvnLabelSize, "Courier New Bold", ANCHOR_RIGHT, false);
     }

   void              DeleteZone(const int i)
     {
      string id = IntegerToString(zones[i].id);
      ObjectDelete(0, HVN_PFX + "z" + id);
      ObjectDelete(0, HVN_PFX + "n" + id);
      ObjectDelete(0, HVN_PFX + "p" + id);
      ObjectDelete(0, HVN_PFX + "l" + id);
      ArrayRemove(zones, i, 1);
     }

   void              Draw(const int i, const datetime &time[], const int rates_total)
     {
      int last = zones[i].endBar >= 0 ? zones[i].endBar : rates_total - 1;
      if(last >= rates_total)
         last = rates_total - 1;
      if(zones[i].startBar < 0 || zones[i].startBar >= rates_total)
         return;
      datetime t1 = time[zones[i].startBar];
      datetime t2 = time[last] + PeriodSeconds();
      string id = IntegerToString(zones[i].id);
      color base = zones[i].primary ? InpHvnPrimary : InpHvnSecondary;
      IdcRect(HVN_PFX + "z" + id, t1, zones[i].top, t2, zones[i].bottom, IdcBlend(base, InpHvnOpacity / 100.0));
      if(InpHvnShowLabels)
         NodeLabels(HVN_PFX + "n" + id, HVN_PFX + "p" + id, t2, (zones[i].top + zones[i].bottom) / 2, zones[i].label, zones[i].buyShare);
      if(InpHvnShowPoc && zones[i].primary)
         IdcLine(HVN_PFX + "l" + id, t1, zones[i].poc + per.rowSize * 0.5, t2, zones[i].poc + per.rowSize * 0.5, InpHvnPocColor, 2, STYLE_SOLID, false);
     }

   //--- symbol tick and the initial row size (rowPoints 0: one tick until SetRow)
   void              Init(const int rowPoints)
     {
      tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      if(tick <= 0)
         tick = _Point;
      SetRow(InpHvnBasePoints > 0 ? MathMax(1, MathRound(InpHvnBasePoints * _Point / tick)) * tick : tick);
      nextId = 0;
      perKey = 0;
      liveBar = -1;
     }

   //--- footprint row size (automatic: IdcAutoRow from the chart's bars)
   void              SetRow(const double row)
     {
      bar_.rowSize = row;
      per.rowSize = row;
     }
  };

#endif
