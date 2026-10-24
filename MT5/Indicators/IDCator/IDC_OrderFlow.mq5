//+------------------------------------------------------------------+
//| IDC Order Flow (MetaTrader 5)                                    |
//| Clean-room replica of the order-flow tools shown in ProfitPro's  |
//| videos; not their code. Not a buy/sell signal.                   |
//|                                                                  |
//| The four IDCator tools in one indicator, each switched on or off |
//| in the inputs: HVN Profile, Imbalance Levels, Heavy Absorption,  |
//| Pressure Toolkit. One tick feed serves all of them; each tool    |
//| draws exactly what its own indicator draws.                      |
//+------------------------------------------------------------------+
#property copyright   "IDCator"
#property version     "1.00"
#property description "HVN Profile, Imbalance Levels, Heavy Absorption and Pressure Toolkit in one indicator, each on/off."
#property indicator_chart_window
#property indicator_buffers 0
#property indicator_plots   0

#include <IDCator/IDCatorCore.mqh>

input group "Tools (on/off)"
input bool             InpOnHvn        = true;           // HVN Profile
input bool             InpOnImb        = true;           // Imbalance Levels
input bool             InpOnHa         = true;           // Heavy Absorption
input bool             InpOnPtk        = false;          // Pressure Toolkit
input group "Data"
input ENUM_IDC_TICKSRC InpTickSource   = IDC_TICKS_AUTO; // Tick source
input int              InpRowPoints    = 0;              // Row size in points (0 = auto)
input int              InpMaxBars      = 3000;           // Bars to load

#include <IDCator/HvnProfile.mqh>
#include <IDCator/ImbalanceLevels.mqh>
#include <IDCator/HeavyAbsorption.mqh>
#include <IDCator/PressureToolkit.mqh>

//--- one tick feed for all tools: every trade, bar close and refresh goes to the tools that are on (in drawing order)
class CIdcOrderFlow : public CIdcFeed
  {
public:
   CHvnProfile       hvn;
   CImbalance        imb;
   CHeavyAbsorption  ha;
   CPressureToolkit  ptk;

   virtual void      OnReset(void)
     {
      if(InpOnHvn)
         hvn.OnReset();
      if(InpOnImb)
         imb.OnReset();
      if(InpOnHa)
         ha.OnReset();
      if(InpOnPtk)
         ptk.OnReset();
     }

   virtual void      OnTrade(const int bar, const IdcTrade &t, const datetime &time[], const int rates_total, const bool live)
     {
      if(InpOnHvn)
         hvn.OnTrade(bar, t, time, rates_total, live);
      if(InpOnImb)
         imb.OnTrade(bar, t, time, rates_total, live);
      if(InpOnHa)
        {
         ha.HostTrades(UsesTrades());
         ha.OnTrade(bar, t, time, rates_total, live);
        }
      if(InpOnPtk)
         ptk.OnTrade(bar, t, time, rates_total, live);
     }

   virtual void      OnBarClose(const int bar, const datetime &time[], const int rates_total, const bool live)
     {
      if(InpOnHvn)
         hvn.OnBarClose(bar, time, rates_total, live);
      if(InpOnImb)
         imb.OnBarClose(bar, time, rates_total, live);
      if(InpOnHa)
         ha.OnBarClose(bar, time, rates_total, live);
      if(InpOnPtk)
         ptk.OnBarClose(bar, time, rates_total, live);
     }

   virtual void      OnRefresh(const datetime &time[], const int rates_total)
     {
      if(InpOnHvn)
         hvn.OnRefresh(time, rates_total);
      if(InpOnImb)
         imb.OnRefresh(time, rates_total);
      if(InpOnHa)
         ha.OnRefresh(time, rates_total);
      if(InpOnPtk)
         ptk.OnRefresh(time, rates_total);
     }
  };

CIdcOrderFlow g_ind;

int OnInit()
  {
   if(InpOnHvn && PeriodSeconds(InpHvnPeriod) <= PeriodSeconds())
      Print("IDC HVN Profile: choose a profile timeframe higher than the chart timeframe");
   g_ind.Setup(InpTickSource, InpMaxBars);
   g_ind.hvn.Init(0);
   g_ind.imb.Init(InpRowPoints);
   g_ind.ha.Init(InpRowPoints);
   g_ind.ptk.Init(InpRowPoints);
   EventSetTimer(1);
   return INIT_SUCCEEDED;
  }

void OnDeinit(const int reason)
  {
   EventKillTimer();
   ObjectsDeleteAll(0, HVN_PFX);
   ObjectsDeleteAll(0, IMB_PFX);
   ObjectsDeleteAll(0, HA_PFX);
   ObjectsDeleteAll(0, PTK_PFX);
   Comment("");
  }

void OnTimer()
  {
   g_ind.Timer();
  }

int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long &tick_volume[],
                const long &volume[],
                const int &spread[])
  {
   // automatic row size from the chart's bars, set at every (re)start; one size for all tools
   if(prev_calculated == 0)
     {
      double row = IdcAutoRow(high, low, rates_total, g_ind.imb.tick);
      if(InpRowPoints <= 0)
        {
         g_ind.imb.SetRow(row);
         g_ind.ha.SetRow(row);
         g_ind.ptk.SetRow(row);
        }
      if(InpHvnBasePoints <= 0)
         g_ind.hvn.SetRow(row);
     }
   if(!g_ind.Update(rates_total, prev_calculated, time))
     {
      Comment(g_ind.Loading() ? "IDC Order Flow: loading tick history..." : "");
      return prev_calculated;
     }
   Comment("");
   ChartRedraw();
   return rates_total;
  }
