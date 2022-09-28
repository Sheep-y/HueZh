using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using ZyMod;
using static ZyMod.ModHelpers;

namespace HueZh {

   public class HueZh : RootMod {

      public static void Main () => new HueZh().Initialize();

      protected override string GetAppDataDir () {
         var path = Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments );
         return string.IsNullOrEmpty( path ) ? null : Path.Combine( Path.Combine( path, "My Games" ), Path.Combine( "Curve Digital", "Hue" ) );
      }

      protected override void OnGameAssemblyLoaded ( Assembly _ ) => new HuePatcher().Apply();
   }

   internal class HuePatcher : Patcher {
      private static string TextPath => Path.Combine( RootMod.AppDataDir, "HueZh.csv" );

      internal void Apply () {
         Patch( typeof( LocalizedText ), "SetToLanguage", nameof( OverrideLocalizedText ) );
         Patch( typeof( Subtitles ), "SetToLanguage", nameof( OverrideSubtitles ) );
      }

      private static string LoadDefaultData () => Encoding.UTF8.GetString( Resource.HueZh_csv );
      private static string LoadData () { lock( TextPath ) {
         if ( File.Exists( TextPath ) ) {
            Info( "Loading data from {0} ({1} bytes).  The file is user editable.  Delete it to reset to default.", TextPath, new FileInfo( TextPath ).Length );
            try { return File.ReadAllText( TextPath ); } catch ( SystemException x ) { Warn( x ); }
         }
         Info( "Loading build-in data ({0} bytes) to recreate {1}.", Resource.HueZh_csv.Length, TextPath );
         try {
            File.WriteAllBytes( TextPath, Resource.HueZh_csv );
            Fine( "{1} bytes written to {0}", TextPath, new FileInfo( TextPath ).Length );
         } catch ( SystemException x ) { Warn( x ); }
         return LoadDefaultData();
      } }

      private static void OverrideLocalizedText ( string[] lines, ref int languageID, ref string ___selectedLanguage ) { try {
         if ( lines == null || lines.Length <= 1 ) return;
         languageID = 1;
         if ( ___selectedLanguage == "chinese" ) { Info( "Game language is already chinese, index {0}.", languageID ); return; }
         Info( "Original game language is {0}.  {1} lines found.", ___selectedLanguage, lines.Length );
         var orig = lines.Clone() as string[];
         ThreadPool.QueueUserWorkItem( ( _ ) => {
            lock ( TextPath ) File.WriteAllText( Path.Combine( RootMod.AppDataDir, "GameText.csv" ), string.Join( "\r\n", orig ) );
         } );
         OverrideLanguage( lines );
         ___selectedLanguage = "chinese";
         lines[ 0 ] = "Column,chinese";
      } catch ( Exception ex ) { Err( ex ); } }

      private static void OverrideSubtitles ( ref int languageID ) => languageID = 1;

      private static void OverrideLanguage ( string[] lines ) { try {
         CsvToDictionary( LoadData(), out var map, out var blank, out var firstRow );
         Info( "Chinese data loaded ({0} translated, {1} keep original).", map.Count, blank.Count );
         if ( map.Count <= 1 || firstRow == null ) { Error( "Too few entries, something is wrong.  Aborting." ); return; }
         CheckUpdate( firstRow, map, blank );
         var count = 0;
         var buffer = new StringBuilder();
         for ( var i = lines.Length - 1 ; i >= 1 ; i-- ) {
            var cells = new StringReader( lines[ i ] ).ReadCsvRow( buffer )?.ToArray();
            if ( cells == null || cells.Length <= 1 || cells[ 0 ].Length == 0 ) continue;
            var key = cells[ 0 ];
            if ( ! map.TryGetValue( key, out var text ) ) {
               cells = new StringReader( lines[ i ] ).ReadCsvRow().ToArray();
               if ( ! blank.Contains( key ) ) Info( "Untranslated: {0} => {1}", key, cells[ 1 ] );
               text = cells[ 1 ].Length == 0 ? "?" : cells[ 1 ];
            } else
               count++;
            Fine( "Replacing {0}", key );
            lines[ i ] = buffer.Clear().AppendCsvLine( key, text ).ToString();
         }
         Info( "{0} translations replaced.", count );
      } catch ( Exception ex ) { Err( ex ); } }

      private static void CsvToDictionary ( string data, out Dictionary< string, string > map, out HashSet< string > blank, out string[] firstLine ) {
         map = new Dictionary< string, string >();
         blank = new HashSet< string >();
         var buffer = new StringBuilder();
         var r = new StringReader( data );
         // First line: check upgrade
         if ( ! r.TryReadCsvRow( out var firstRow, buffer ) ) { firstLine = null; return; }
         firstLine = firstRow.ToArray();
         while ( r.TryReadCsvRow( out var line, buffer ) ) {
            var cells = line.ToArray();
            if ( cells.Length < 2 || cells[ 0 ].Length == 0 || ( cells[ 0 ] == "Column" && cells[ 1 ] == "english" ) ) continue;
            if ( cells[ 1 ].Length == 0 ) { blank.Add( cells[ 0 ] ); continue; }
            Fine( "Loaded {0} ({1})", cells[ 0 ], cells[ 1 ].Length );
            map[ cells[ 0 ] ] = cells[ 1 ].Replace( '\r', ' ' ).Replace( '\n', ' ' );
         }
      }

      private static int TextUpdated;
      private static void CheckUpdate ( string[] cells, Dictionary< string, string > map, HashSet< string > blank ) { try {
         if ( cells.Length != 2 || cells[ 1 ] != "chinese" ) {
            Warn( "First row is malformed.  Expected {version},chinese.  Translation will not be updated." );
            return;
         }
         Fine( "Checking translation updates." );
         CsvToDictionary( LoadDefaultData(), out var latest, out var _, out var _ );
         UpdateString( map, latest, "LighthouseKeeperAquaC2", "是有另一個秘密入口，不過……" );
         UpdateString( map, latest, "LighthouseKeeperAquaC3", "……距離這兒很遠。" );
         UpdateString( map, latest, "WaterLadyA1", "我嚇倒我了……" );
         UpdateString( map, latest, "WaterLadyA2", "我？我走累了，在休息。" );
         UpdateString( map, latest, "FishermanMountainsB0", "往東走，就會看見大學的路牌。" );
         UpdateString( map, latest, "FishermanMountainsD0", "聽說紅船跟藍船撞在一起了……" );
         UpdateString( map, latest, "LumberjackC2", "我留了一些柴，在大門裡面的大石那邊。" );
         UpdateString( map, latest, "LumberjackC3", "有人不停堅持將這個燒杯放在這兒。" );
         UpdateString( map, latest, "VO/Fire3" , "我們分離光線、紀錄光譜、一起繪影、一起歡笑……我們日夜工作，向目標直奔。" );
         UpdateString( map, latest, "VO/Fire4" , "我們是代理人。我們心知肚明，是項研究比我們更重要。" );
         UpdateString( map, latest, "VO/Intro3", "吃裏扒外的灰博士意圖偷走我開發的環彩光戒。用它能觀察和改變色彩。" );
         UpdateString( map, latest, "VO/Intro8", "我在家裡觀察了數星期，在看、在等。" );
         UpdateString( map, latest, "VO/Intro10", "所以我走了。我會去大學，找我私藏的自制色彩道具。" );
         UpdateString( map, latest, "VO/Water0", "當你來這兒看瀑布時，你大概會得償所願。" );
         UpdateString( map, latest, "VO/Water1", "但是假若放下成見，就能找到源源不絕的驚喜，你說不是嗎？" );
         UpdateString( map, latest, "VO/Water4", "就是你不被現象蒙蔽，看透世事的可能性。" );
         UpdateString( map, latest, "VO/Red0", "你知道嗎，色調，所有語言都先有'黑'字和'白'字、'光'和'暗'？" );
         UpdateString( map, latest, "VO/Red4", "真滑稽。我們有語文，有筆墨紙硯，卻時而無法溝通。" );
         UpdateString( map, latest, "VO/Blue2", "文書所載的奇事異象，有一件事格格不入……那就是藍。他們沒有藍。" );
         UpdateString( map, latest, "VO/TechEnd1", "世界的真彩，並非世界的真相。" );
         UpdateString( map, latest, "VO/TechEnd2", "真理之道無窮無盡，永遠有新的目標。" );
         UpdateString( map, latest, "VO/TechEnd3", "這些新的色彩……超脫我的狂想，卻多如繁星。" );
         UpdateString( map, latest, "VO/TechEnd4", "真相是真實的嗎，色調？會不會，真相只是思維概念，如同色彩？" );
         UpdateString( map, latest, "VO/TechEnd5", "說不定我有一天會遇上三次元，哈！試想像一下！" );
         UpdateString( map, latest, "VO/UniversityStart2", "灰博士會從講臺上向下望，瞬間脫去他的嚴肅和權威。" );
         UpdateString( map, latest, "VO/UniversityEnd1", "你的媽媽……沒聽我的提醒。" );
         UpdateString( map, latest, "VO/UniversityEnd6", "當我擊碎戒指時，也同時改變了你的媽媽的根源。" );
         UpdateString( map, latest, "VO/PS1", "既然你能看見真彩，你得決定這是好是壞。" );
         UpdateString( map, latest, "VO/PS2", "經歷了這麼多，我衷心希望是好的。" );
         //UpdateString( map, latest, "", "" );
         if ( cells[ 0 ] == "Column" ) { // Original release.
            if ( map.TryGetValue( "CreditCurveJuniorPrCommunity", out var txt ) && txt == "RP et community junior" ) {
               Info( "Clearing CreditCurveJuniorPrCommunity" );
               map.Remove( "CreditCurveJuniorPrCommunity" );
               blank.Add( "CreditCurveJuniorPrCommunity" );
            }
            TextUpdated++; // Update version number.
         }
         if ( TextUpdated > 0 ) try { lock( TextPath ) { // Write file back
            Info( "{0} translations updated.  Saving to {1}", TextUpdated, TextPath );
            File.Delete( TextPath );
            using ( var writer = new StreamWriter( TextPath ) ) {
               writer.WriteCsvLine( "20220928", "chinese" );
               foreach ( var entries in map )
                  writer.WriteCsvLine( entries.Key, entries.Value );
               foreach ( var entries in blank )
                  writer.WriteCsvLine( entries, "" );
            }
         } } catch ( Exception x ) {
            Warn( x );
         }
      } catch ( Exception ex ) { Err( ex ); } }

      private static void UpdateString ( Dictionary< string, string > map, Dictionary< string, string > latest, string key, params string[] from ) {
         if ( ! map.TryGetValue( key, out var oldTxt ) || ! latest.TryGetValue( key, out var newTxt ) || oldTxt == newTxt ) return;
         if ( Array.IndexOf( from, oldTxt ) < 0 ) {
            Warn( "Skipped: {0} is currently {1}", key, oldTxt );
            Warn( "Latest translation is {0}" );
            return;
         }
         Info( "Updating {0} from {1} to {2}", key, from, newTxt );
         map[ key ] = newTxt;
         TextUpdated++;
      }
   }
}