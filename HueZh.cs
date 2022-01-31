﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

      private static string ReadData () {
         if ( File.Exists( TextPath ) ) {
            Info( "Loading data from {0} ({1} bytes).  The file is user editable.  Delete it to reset to default.", TextPath, new FileInfo( TextPath ).Length );
            try { return File.ReadAllText( TextPath ); } catch ( SystemException x ) { Warn( x ); }
         }
         Info( "Loading from build-in data ({0} bytes).", Resource.HueZh_csv.Length );
         var data = Encoding.UTF8.GetString( Resource.HueZh_csv );
         Info( "Recreating {0}", TextPath );
         try {
            File.WriteAllBytes( TextPath, Resource.HueZh_csv );
            Fine( "{1} bytes written to {0}", TextPath, new FileInfo( TextPath ).Length );
         } catch ( SystemException x ) { Warn( x ); }
         return data;
      }

      private static void OverrideLanguage ( string[] lines, ref int languageID, ref string ___selectedLanguage, Dictionary< string, int > ___languages ) { try {
         if ( lines == null || lines.Length <= 1 ) return;
         if ( ___selectedLanguage == "chinese" ) { Info( "Game language is already chinese, index {0}.", languageID ); return; }
         Info( "Original game language is {0}.  {1} lines found.", ___selectedLanguage, lines.Length );
         #if DEBUG
         File.WriteAllText( Path.Combine( RootMod.AppDataDir, "Orig.csv" ), string.Join( "\r\n", lines ) );
         #endif

         var map = new Dictionary< string, string >();
         var blank = new HashSet< string >();
         var buffer = new StringBuilder();
         var r = new StringReader( ReadData() );
         while ( r.TryReadCsvRow( out var line, buffer ) ) {
            var cells = line.ToArray();
            if ( cells.Length < 3 || cells[ 0 ].Length == 0 || ( cells[ 0 ] == "Column" && cells[ 1 ] == "english" ) ) continue;
            if ( cells[ 2 ].Length == 0 ) { blank.Add( cells[ 0 ] ); continue; }
            Fine( "Loaded {0}", cells[ 0 ] );
            map[ cells[ 0 ] ] = cells[ 2 ].Replace( '\r', ' ' ).Replace( '\n', ' ' );
         }
         Info( "Chinese data loaded ({0} translated, {1} keep original).", map.Count, blank.Count );
         if ( map.Count <= 1 ) {
            Error( "Too few entries, something is wrong.  Aborting." );
            return;
         }

         for ( var i = lines.Length - 1 ; i >= 1 ; i-- ) {
            var cells = new StringReader( lines[ i ] ).ReadCsvRow( buffer ).ToArray();
            if ( cells.Length <= 1 || cells[ 0 ].Length == 0 ) continue;
            var key = cells[ 0 ];
            if ( ! map.TryGetValue( key, out var text ) ) {
               cells = new StringReader( lines[ i ] ).ReadCsvRow().ToArray();
               if ( ! blank.Contains( key ) ) Info( "Untranslated: {0} => {1}", key, cells[ 1 ] );
               text = cells[ 1 ].Length == 0 ? "?" : cells[ 1 ];
            }
            var line = new StringBuilder().AppendCsvLine( key, text ).ToString();
            Fine( "Updating {0}", key );
            lines[ i ] = line;
         }

         Info( "Chinese data injected.  Changing game language to chinese." );
         languageID = 1;
         ___selectedLanguage = "chinese";
         lines[0] = "Column,english";
         if ( ___languages.TryGetValue( "english", out var pos ) && pos == 1 ) {
            ___languages.Clear();
            ___languages.Add( "chinese", 1 );
         } else
            Warn( "Unexpected game language list; english not at col 1: {0}", string.Join( ", ", ___languages.OrderBy( e => e.Value ).Select( e => $"{e.Value}:{e.Key}" ).ToArray() ) );
      } catch ( Exception ex ) { Err( ex ); } }
   }

}