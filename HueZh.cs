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
         return string.IsNullOrEmpty( path ) ? null : Path.Combine( Path.Combine( path, "My Games" ), "Joyful Hant" );
      }

      protected override void OnGameAssemblyLoaded ( Assembly _ ) => new HuePatcher().Apply();
   }

   internal class HuePatcher : Patcher {
      private const string TextPath = "HueZh.csv";

      internal void Apply () => Patch( typeof( LocalizedText ), "SetToLanguage", nameof( OverrideLanguage ) );

      private void OverrideLanguage ( string[] lines, ref int languageID, ref string ___selectedLanguage, Dictionary< string, int > ___languages  ) { try {
         Info( "Original game language is {0}.  Changing to chinese.", ___selectedLanguage );

         var comma = new char[]{ ',' };
         var map = new Dictionary< string, string >();
         foreach ( var line in File.ReadAllText( TextPath ).Split( new string[]{ "\r\n" }, StringSplitOptions.None ) ) {
            if ( line.Length == 0 ) continue;
            var cells = line.Split( comma, 2 );
            if ( cells.Length <= 1 || cells[ 0 ].Length == 0 ) continue;
            Debug.Assert( cells[0][0] != '"' );
            map[ cells[ 0 ] ] = line;
         }

         for ( var i = lines.Length - 1 ; i >= 0 ; i++ ) {
            var cells = lines[ i ].Split( comma, 3 );
            if ( cells.Length <= 1 || cells[ 0 ].Length == 0 ) continue;
            Debug.Assert( cells[0][0] != '"' );
            if ( ! map.TryGetValue( cells[ 0 ], out var line ) ) {
               cells = new StringReader( lines[ i ] ).ReadCsvRow().ToArray();
               Info( "Untranslated: {0} => {1}", cells[0], cells[1] );
               line = new StringBuilder().AppendCsvLine( cells[ 0 ], cells[ 1 ], cells[ 1 ] ).ToString();
            }
            lines[ i ] = line;
         }

         languageID = 1;
         ___selectedLanguage = "chinese";
         if ( ___languages.TryGetValue( "english", out var pos ) && pos == 0 ) {
            ___languages.Clear();
            ___languages.Add( "english", 0 );
            ___languages.Add( "chinese", 0 );
         } else
            Warn( "Unexpected game language list: {0}", string.Join( ", ", ___languages.OrderBy( e => e.Value ).Select( e => $"{e.Value}:{e.Key}" ) ) );

      } catch ( FileNotFoundException ex ) {
         Error( "Not Found: {0}", TextPath );
      } catch ( Exception ex ) {
         Err( ex );
      } }
   }

}