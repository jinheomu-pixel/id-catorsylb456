// IDCator MT5 simulation driver, appended by `check.py --run` to a translated indicator.
//
//   sim TICKS.csv OUT.json [--hist N] [--batch N] [--quiet SECONDS] [--snap I,J,...] [--set Name=Value ...]
//
// TICKS.csv: time_msc,bid,ask,last,volume,flags (MqlTick fields), oldest first.
// The first N ticks (--hist) are history when the indicator is attached; every later tick arrives live and
// OnCalculate runs after every --batch ticks. OnTimer fires every second of the simulated clock, also during
// --quiet seconds after the last tick. --snap writes the chart objects after the given tick numbers.
#include <csignal>
#include <fstream>
#include <sstream>

static MqlArr<datetime> s_time;
static MqlArr<double> s_open, s_high, s_low, s_close;
static MqlArr<long> s_tickVolume, s_volume;
static MqlArr<int> s_spread;
static datetime s_lastTimer = 0;

static void SimSync()
  {
   size_t n = Sim::bars.size();
   size_t from = s_time.v.empty() ? 0 : s_time.v.size() - 1;   // the forming bar may have changed
   s_time.v.resize(n);
   s_open.v.resize(n);
   s_high.v.resize(n);
   s_low.v.resize(n);
   s_close.v.resize(n);
   s_tickVolume.v.resize(n);
   s_volume.v.resize(n);
   s_spread.v.resize(n);
   for(size_t i = from; i < n; i++)
     {
      const Sim::Bar &b = Sim::bars[i];
      s_time.v[i] = b.t;
      s_open.v[i] = b.o;
      s_high.v[i] = b.h;
      s_low.v[i] = b.l;
      s_close.v[i] = b.c;
      s_tickVolume.v[i] = b.tv;
      s_volume.v[i] = 0;
      s_spread.v[i] = 0;
     }
  }

static int SimCalc(int prev)
  {
   SimSync();
   return OnCalculate((int)Sim::bars.size(), prev, s_time, s_open, s_high, s_low, s_close, s_tickVolume, s_volume, s_spread);
  }

//--- moves the clock to `wall`, firing the timer on the way
static void SimClock(datetime wall)
  {
   if(s_lastTimer == 0)
      s_lastTimer = wall;
   while(Sim::timerSeconds > 0 && s_lastTimer + Sim::timerSeconds <= wall)
     {
      s_lastTimer += Sim::timerSeconds;
      Sim::wall = s_lastTimer;
      OnTimer();
     }
   Sim::wall = std::max(Sim::wall, wall);
  }

static std::string SimJson(const std::string &s)
  {
   std::string o = "\"";
   for(unsigned char c : s)
     {
      if(c == '"' || c == '\\')
        {
         o += '\\';
         o += (char)c;
        }
      else
         if(c < 0x20)
           {
            char b[8];
            std::snprintf(b, sizeof(b), "\\u%04x", c);
            o += b;
           }
         else
            o += (char)c;
     }
   return o + "\"";
  }

static std::string SimColor(long c)
  {
   char b[16];
   std::snprintf(b, sizeof(b), "\"#%02x%02x%02x\"", (int)(c & 255), (int)((c >> 8) & 255), (int)((c >> 16) & 255));
   return b;
  }

static std::string SimObjects()
  {
   std::vector<std::pair<std::string, const Sim::Obj *>> list;
   for(const auto &kv : Sim::objects)
      list.push_back({kv.first, &kv.second});
   std::sort(list.begin(), list.end(), [](const auto &a, const auto &b) { return a.second->seq < b.second->seq; });
   std::ostringstream o;
   o.precision(12);
   o << "[";
   bool first = true;
   for(const auto &kv : list)
     {
      const Sim::Obj &x = *kv.second;
      auto get = [&](int p, long d) { auto it = x.ints.find(p); return it == x.ints.end() ? d : it->second; };
      static const char *types[] = {"rect", "text", "trend", "hline", "label"};
      o << (first ? "" : ",") << "\n {\"name\":" << SimJson(kv.first) << ",\"type\":\"" << types[x.type] << "\""
        << ",\"t1\":" << x.time[0] << ",\"t2\":" << x.time[1] << ",\"p1\":" << x.price[0] << ",\"p2\":" << x.price[1]
        << ",\"color\":" << SimColor(get(OBJPROP_COLOR, 0)) << ",\"text\":" << SimJson(x.text) << ",\"font\":" << SimJson(x.font)
        << ",\"size\":" << get(OBJPROP_FONTSIZE, 10) << ",\"anchor\":" << get(OBJPROP_ANCHOR, 0) << ",\"width\":" << get(OBJPROP_WIDTH, 1)
        << ",\"style\":" << get(OBJPROP_STYLE, 0) << ",\"back\":" << get(OBJPROP_BACK, 0) << ",\"ray\":" << get(OBJPROP_RAY_RIGHT, 0) << "}";
      first = false;
     }
   o << "]";
   return o.str();
  }

static void SimZeroDivide(int)
  {
   std::fprintf(stderr, "MQL5 CRITICAL ERROR: zero divide\n");
   std::_Exit(3);
  }

int main(int argc, char **argv)
  {
   if(argc < 3)
     {
      std::fprintf(stderr, "usage: %s TICKS.csv OUT.json [--hist N] [--batch N] [--quiet S] [--snap I,J] [--set Name=Value]\n", argv[0]);
      return 2;
     }
   long hist = -1, batch = 1, quiet = 0;
   std::vector<long> snaps;
   for(int i = 3; i < argc; i++)
     {
      std::string a = argv[i];
      std::string v = i + 1 < argc ? argv[i + 1] : "";
      if(a == "--hist")
         hist = std::stol(v), i++;
      else
         if(a == "--batch")
            batch = std::max(1L, std::stol(v)), i++;
         else
            if(a == "--quiet")
               quiet = std::stol(v), i++;
            else
               if(a == "--snap")
                 {
                  std::stringstream ss(v);
                  std::string x;
                  while(std::getline(ss, x, ','))
                     snaps.push_back(std::stol(x));
                  i++;
                 }
               else
                  if(a == "--set")
                    {
                     size_t eq = v.find('=');
                     if(eq == std::string::npos || !SimSetInput(v.substr(0, eq), v.substr(eq + 1)))
                       {
                        std::fprintf(stderr, "unknown input: %s\n", v.c_str());
                        return 2;
                       }
                     i++;
                    }
     }

   std::ifstream in(argv[1]);
   std::string line;
   bool anyLast = false;
   while(std::getline(in, line))
     {
      if(line.empty() || !(std::isdigit((unsigned char)line[0])))
         continue;
      MqlTick k{};
      unsigned long long vol = 0;
      unsigned int flags = 0;
      if(std::sscanf(line.c_str(), "%lld,%lf,%lf,%lf,%llu,%u", &k.time_msc, &k.bid, &k.ask, &k.last, &vol, &flags) != 6)
         continue;
      k.time = k.time_msc / 1000;
      k.volume = vol;
      k.volume_real = (double)vol;
      k.flags = flags;
      anyLast = anyLast || (flags & TICK_FLAG_LAST) != 0;
      Sim::ticks.push_back(k);
     }
   if(Sim::ticks.empty())
     {
      std::fprintf(stderr, "no ticks in %s\n", argv[1]);
      return 2;
     }
   Sim::barsFromLast = anyLast;
   if(hist < 0 || hist > (long)Sim::ticks.size())
      hist = (long)Sim::ticks.size();

   std::signal(SIGFPE, SimZeroDivide);
   feenableexcept(FE_DIVBYZERO);

   // history: ticks that are already in the terminal when the indicator is attached
   while((long)Sim::avail < hist)
      Sim::Arrive();
   Sim::wall = TimeCurrent();
   s_lastTimer = Sim::wall;
   if(OnInit() != INIT_SUCCEEDED)
     {
      std::fprintf(stderr, "OnInit failed\n");
      return 4;
     }
   int prev = SimCalc(0);

   std::ostringstream snapJson;
   snapJson.precision(12);
   bool firstSnap = true;
   // live: one tick at a time
   for(size_t i = (size_t)hist; i < Sim::ticks.size(); i++)
     {
      SimClock(Sim::ticks[i].time);
      Sim::Arrive();
      if((long)(i + 1 - hist) % batch == 0 || i + 1 == Sim::ticks.size())
         prev = SimCalc(prev);
      if(std::find(snaps.begin(), snaps.end(), (long)i) != snaps.end())
        {
         snapJson << (firstSnap ? "" : ",") << "\n{\"tick\":" << i << ",\"wall\":" << Sim::wall << ",\"objects\":" << SimObjects() << "}";
         firstSnap = false;
        }
     }
   // a quiet market after the last tick: only the timer runs
   if(quiet > 0)
      SimClock(Sim::wall + quiet);

   std::ofstream out(argv[2]);
   out.precision(12);
   out << "{\"period\":60,\"bars\":[";
   for(size_t i = 0; i < Sim::bars.size(); i++)
     {
      const Sim::Bar &b = Sim::bars[i];
      out << (i ? "," : "") << "[" << b.t << "," << b.o << "," << b.h << "," << b.l << "," << b.c << "]";
     }
   out << "],\n\"objects\":" << SimObjects() << ",\n\"alerts\":[";
   for(size_t i = 0; i < Sim::alerts.size(); i++)
      out << (i ? "," : "") << "\n{\"wall\":" << Sim::alerts[i].first << ",\"msg\":" << SimJson(Sim::alerts[i].second) << "}";
   out << "],\n\"comment\":" << SimJson(Sim::comment) << ",\n\"snapshots\":[" << snapJson.str() << "]}\n";
   return 0;
  }
