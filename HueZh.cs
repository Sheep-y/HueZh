using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
      private static string TextPath = Path.Combine( Path.GetDirectoryName( Assembly.GetExecutingAssembly().Location ), "HueZh.csv" );

      internal void Apply () => Patch( typeof( LocalizedText ), "SetToLanguage", nameof( OverrideLanguage ) );

      private static void OverrideLanguage ( string[] lines, ref int languageID, ref string ___selectedLanguage, Dictionary< string, int > ___languages  ) { try {
         if ( lines == null ) return;
         Info( "Original game language is {0}.  {1} lines found.", ___selectedLanguage, lines.Length );

         var comma = new char[]{ ',' };
         var map = new Dictionary< string, string >();
         foreach ( var line in File.ReadAllText( TextPath ).Split( new string[]{ "\r\n" }, StringSplitOptions.None ) ) {
            if ( line.Length == 0 ) continue;
            var cells = line.Split( comma, 2, StringSplitOptions.None );
            if ( cells.Length <= 1 || cells[ 0 ].Length == 0 ) continue;
            Fine( "Loaded {0}", cells[0] );
            Debug.Assert( cells[0][0] != '"' );
            map[ cells[ 0 ] ] = line;
         }
         Info( "Chinese data loaded." );

         for ( var i = lines.Length - 1 ; i >= 0 ; i-- ) {
            var cells = lines[ i ].Split( comma, 2, StringSplitOptions.None );
            if ( cells.Length <= 1 || cells[ 0 ].Length == 0 ) continue;
            Debug.Assert( cells[0][0] != '"' );
            if ( ! map.TryGetValue( cells[ 0 ], out var line ) ) {
               cells = new StringReader( lines[ i ] ).ReadCsvRow().ToArray();
               Info( "Untranslated: {0} => {1}", cells[0], cells[1] );
               line = new StringBuilder().AppendCsvLine( cells[ 0 ], cells[ 1 ], cells[ 1 ] ).ToString();
            } else
               Fine( "Updating {0}", cells[ 0 ] );
            lines[ i ] = line;
         }

         languageID = 2;
         ___selectedLanguage = "chinese";
         Info( "Chinese data added to game.  Changing game langauge to chinese." );
         if ( ___languages.TryGetValue( "english", out var pos ) && pos == 1 ) {
            ___languages.Clear();
            ___languages.Add( "english", 1 );
            ___languages.Add( "chinese", 2 );
         } else
            Warn( "Unexpected game language list: {0}", string.Join( ", ", ___languages.OrderBy( e => e.Value ).Select( e => $"{e.Value}:{e.Key}" ).ToArray() ) );

      } catch ( FileNotFoundException ex ) {
         Error( "Not Found: {0}", TextPath );
      } catch ( Exception ex ) {
         Err( ex );
      } }
   }

}