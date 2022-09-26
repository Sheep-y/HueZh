using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using static System.Diagnostics.TraceLevel;
using static System.Reflection.BindingFlags;

#if ! NoPatch
using HarmonyLib;
using static HarmonyLib.HarmonyPatchType;
#endif

// Sheepy's "Universal" skeleton mod and tools.  No depency other than Harmony2 / HarmonyX.
// Bootstrap, Background Logging, Roundtrip Config, Reflection, Manual Patcher with Unpatch.
namespace ZyMod {
   using LogFunc = Action< TraceLevel, object, object[] >;

   // All the important mod data in one place.  
   public class ModComponent {
      protected static readonly object sync = new object();
      public static string ModName, ModPath, ModDir, AppDataDir;
      public static LogFunc _Logger;
      public static LogFunc Logger { get { lock ( sync ) return _Logger; } set { lock ( sync ) _Logger = value; } }
      public static void Err ( object msg ) => Error( msg );
      public static T Err < T > ( object msg, T val ) { Error( msg ); return val; }
      public static void Error ( object msg, params object[] arg ) => Log( TraceLevel.Error, msg, arg );
      public static void Warn  ( object msg, params object[] arg ) => Log( TraceLevel.Warning, msg, arg );
      public static void Info  ( object msg, params object[] arg ) => Log( TraceLevel.Info, msg, arg );
      public static void Fine  ( object msg, params object[] arg ) => Log( TraceLevel.Verbose, msg, arg );
      public static void Log   ( TraceLevel lv, object msg, params object[] arg ) => Logger?.Invoke( lv, msg, arg );
      #if ! NoLog
      public static ZyLogger ZyLog;
      #endif
   }

   public abstract class RootMod : ModComponent {
      protected static RootMod instance;

      protected virtual bool IgnoreAssembly ( Assembly asm ) => string.Equals( asm.GetType().FullName, "System.Reflection.Emit.AssemblyBuilder" ) || asm.FullName.StartsWith( "DMDASM." ) || asm.FullName.StartsWith( "HarmonyDTFAssembly" );
      protected virtual bool IsTargetAssembly ( Assembly asm ) => asm.GetName().Name == "Assembly-CSharp"; // If overrode, OnGameAssemblyLoaded may be called mutliple times

      public void Initialize () {
         lock ( sync ) { if ( instance != null ) { Warn( "Mod already initialized" ); return; } instance = this; }
         try {
            SetModIO();
#if ! NoBootstrap
            AppDomain.CurrentDomain.AssemblyLoad += AsmLoaded;
            if ( shouldLogAssembly ) {
               AppDomain.CurrentDomain.UnhandledException += ( _, evt ) => Error( evt.ExceptionObject );
               AppDomain.CurrentDomain.AssemblyResolve += ( _, evt ) => { Fine( "Resolving {0}", evt.Name ); return null; };
            }
            foreach ( var asm in AppDomain.CurrentDomain.GetAssemblies().ToArray() ) AsmLoaded( asm );
            Info( "Mod Initiated" );
         } catch ( Exception ex ) {
            Error( ex );
         }
      }
      protected bool shouldLogAssembly = true;

      private void AsmLoaded ( object sender, AssemblyLoadEventArgs evt ) => AsmLoaded( evt.LoadedAssembly );
      private void AsmLoaded ( Assembly asm ) {
         if ( IgnoreAssembly( asm ) ) return;
         if ( shouldLogAssembly ) Fine( "DLL {0}, {1}", asm.FullName, asm.CodeBase );
         if ( ! IsTargetAssembly( asm ) ) return;
         GameLoaded( asm );
         if ( ! shouldLogAssembly ) AppDomain.CurrentDomain.AssemblyLoad -= AsmLoaded;
      }

      private void GameLoaded ( Assembly asm ) { try {
         Info( "Target assembly loaded." );
#else
         var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( e => ! IgnoreAssembly( e ) && IsTargetAssembly( e ) );
#endif
         OnGameAssemblyLoaded( asm );
         #if ! NoPatch
         CountPatches();
         #endif
      } catch ( Exception ex ) { Error( ex ); } }

      #if ! NoPatch
      protected virtual void CountPatches () {
         var patches = new Harmony( ModName ).GetPatchedMethods().Select( e => Harmony.GetPatchInfo( e ) );
         Info( "Bootstrap complete.  Patched {0} methods with {1} patches.", patches.Count(), patches.Sum( e => e.Prefixes.Count + e.Postfixes.Count + e.Transpilers.Count ) );
      }
      #endif

      protected virtual void SetModIO () { lock ( sync ) {
         if ( ModName == null ) ModName = GetModName() ?? "ZyMod";
         if ( ModPath == null ) ModPath = new Uri( Assembly.GetExecutingAssembly().CodeBase ).LocalPath;
         if ( ModDir == null ) ModDir = Path.GetDirectoryName( ModPath );
         if ( AppDataDir == null ) {
            AppDataDir = instance.GetAppDataDir();
            if ( ModHelpers.IsBlank( AppDataDir ) )
               AppDataDir = "";
            else try {
               if ( ! Directory.Exists( AppDataDir ) ) {
                  Directory.CreateDirectory( AppDataDir );
                  if ( ! Directory.Exists( AppDataDir ) ) AppDataDir = "";
               }
            } catch ( Exception ) { AppDataDir = ""; }
         }
         #if ! NoLog
         if ( Logger == null ) SetLogger();
         #endif
      } }

      protected virtual void SetLogger () { try {
         string path = Path.Combine( AppDataDir, ModName + ".log" ), hint = ModName + ',' + path;
         if ( ZyLog == null ) ZyLog = new ZyLogger( path );
         Logger = ZyLog.Write;
         // Write a hint file to direct users to mod log, if path is not game folder.
         var hintFile = "ZyMods.log";
         if ( string.IsNullOrEmpty( AppDataDir ) ) return;
         if ( File.Exists( hintFile ) ) using ( var reader = new StreamReader( hintFile ) ) {
            string line = reader.ReadLine();
            while ( line != null ) {
               if ( line.Equals( hint ) ) return; // Skip writing if mod log is already recorded
               line = reader.ReadLine();
            }
         } else
            File.WriteAllText( hintFile, "A list of mods and log locations, to help you find the logs.\r\nList may be incomplete or outdated.  Delete this file to refresh.\r\n" );
         File.AppendAllText( hintFile, "\r\n" + hint );
      } catch ( Exception x ) { Warn( x ); } }

      // Override / Implement these to change mod name, log dir, what to do on Assembly-CSharp, and where patches are located by Modder.
      protected virtual string GetModName () => GetType().Name;
      protected abstract string GetAppDataDir (); // Called once on start.  At most once per thread.  Result will be cached.
      protected abstract void OnGameAssemblyLoaded ( Assembly game ); // Put all the actions here.
   }

   public static class ModHelpers { // Assorted helpers
      public static bool Non0 ( float val ) => val != 0 && Rational( val );
      public static bool Rational ( float val ) => ! float.IsNaN( val ) && ! float.IsInfinity( val );

      public static IEnumerable< MethodInfo > Methods ( this Type type ) => type?.GetMethods( Public | NonPublic | Instance | Static | DeclaredOnly ).Where( e => ! e.IsAbstract );
      public static IEnumerable< MethodInfo > Methods ( this Type type, string name ) => type?.Methods().Where( e => e.Name == name );

      public static MethodInfo Method ( this Type type, string name ) => type?.GetMethod( name, Public | NonPublic | Instance | Static | DeclaredOnly );
      public static MethodInfo Method ( this Type type, string name, int param_count ) => Methods( type, name ).FirstOrDefault( e => e.GetParameters().Length == param_count );
      public static MethodInfo Method ( this Type type, string name, params Type[] types ) => type?.GetMethod( name, Public | NonPublic | Instance | Static | DeclaredOnly, null, types ?? Type.EmptyTypes, null );
      public static MethodInfo TryMethod ( this Type type, string name ) { try { return Method( type, name ); } catch ( Exception ) { return null; } }
      public static object Run ( this MethodInfo func, object self, params object[] args ) => func.Invoke( self, args );
      public static object RunStatic ( this MethodInfo func, params object[] args ) => func.Invoke( null, args );
      public static object TryRun ( this MethodInfo func, object self, params object[] args ) { try { return Run( func, self, args ); } catch ( Exception x ) { return x; } }
      public static object TryRun ( this object self, string name, params object[] args ) { try { return self.GetType().Method( name, args.Length ).Run( self, args ); } catch ( Exception x ) { return x; } }
      public static object TryRunStatic ( this MethodInfo func, params object[] args ) { try { return RunStatic( func, args ); } catch ( Exception x ) { return x; } }
      public static object TryRunStatic ( this Type self, string name, params object[] args ) { try { return self.Method( name, args.Length ).RunStatic( args ); } catch ( Exception x ) { return x; } }
      public static FieldInfo  Field ( this Type type, string name ) => type?.GetField( name, Public | NonPublic | Instance | Static | DeclaredOnly );
      public static PropertyInfo Property ( this Type type, string name ) => type?.GetProperty( name, Public | NonPublic | Instance | Static | DeclaredOnly );

      #if CIL
      private static MethodInfo GetILs, EnumMoveNext;
      // Find the instructions of a method.  Return null on failure.  TODO: Does not work on HarmonyX
      public static IEnumerable< CodeInstruction > GetCodes ( this MethodBase subject ) {
         if ( subject == null ) return null;
         if ( GetILs == null ) GetILs = typeof( Harmony ).Assembly.GetType( "HarmonyLib.MethodBodyReader" )?.Method( "GetInstructions", typeof( ILGenerator ), typeof( MethodBase ) );
         var list = GetILs?.TryRunStatic( null, subject ) as IList;
         var args = list?.GetType().GenericTypeArguments;
         if ( list == null || list.Count == 0 || args.Length == 0 ) return null;
         var code = args[ 0 ].Method( "GetCodeInstruction", 0 );
         return list.Cast<object>().Select( e => code?.Run( e ) as CodeInstruction );
      }
      // Find the MoveNext method of an iterator method.
      public static MethodInfo MoveNext ( this MethodBase subject ) {
         if ( subject == null ) return null;
         if ( EnumMoveNext != null ) return EnumMoveNext.RunStatic( subject ) as MethodInfo;
         else if ( GetILs == null ) {
            EnumMoveNext = typeof( AccessTools ).Method( "EnumeratorMoveNext", typeof( MethodBase ) );
            if ( EnumMoveNext != null ) return MoveNext( subject );
         }
         var op = subject.GetCodes()?.FirstOrDefault( e => e?.opcode.Name == "newobj" );
         return ( op.operand as ConstructorInfo )?.DeclaringType.Method( "MoveNext", 0 );
      }
      #endif

      #if ! NoConfig
      public static bool TryParse ( Type valueType, string val, out object parsed, LogFunc logger = null ) { parsed = null; try {
         if ( valueType == typeof( string ) ) { parsed = val; return true; }
         if ( IsBlank( val ) || val == "null" ) return ! ( valueType.IsValueType || valueType.IsEnum );
         switch ( valueType.FullName ) {
            case "System.Boolean" : switch ( val.ToLowerInvariant() ) {
                                    case "true" : case "yes" : case "1" : parsed = true ; break;
                                    case "false" : case "no" : case "0" : parsed = false; break;
                                    } break;
            case "System.DateTime" :
               if ( DateTime.TryParse( val, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt ) ) parsed = dt;
               break;
            default :
               if ( valueType.IsValueType ) try {
                  parsed = Convert.ChangeType( val, valueType );
                  return true;
               } catch ( SystemException ) { return false; }
               logger?.Invoke( Warning, new NotImplementedException( "Unsupported field type " + valueType.FullName ), null );
               break;
         }
         return parsed != null;
      } catch ( ArgumentException ) {
         logger?.Invoke( Warning, "Invalid value for {0}: {1}", new object[]{ valueType.FullName, val } );
         return false;
      } }
      #endif

      #if ! NoCsv
      /** Write data as a csv row, and then start a new line.  Null will be written as "null". */
      public static TextWriter WriteCsvLine ( this TextWriter tw, params object[] values ) {
         tw.Write( new StringBuilder().AppendCsvLine( values ).Append( "\r\n" ) );
         return tw;
      }

      /** Append data as a csv row, on a new line if builder is non-empty.  Null will be written as "null". */
      public static StringBuilder AppendCsvLine ( this StringBuilder buf, params object[] values ) {
         if ( buf.Length > 0 ) buf.Append( "\r\n" );
         foreach ( var val in values ) {
            string v = val?.ToString() ?? "null";
            if ( v.IndexOfAny( NeedCsvQuote ) >= 0 ) buf.Append( '"' ).Append( v.Replace( "\"", "\"\"" ) ).Append( "\"," );
            else buf.Append( v ).Append( ',' );
         }
         if ( values.Length > 0 ) --buf.Length;
         return buf;
      }
      private static char[] NeedCsvQuote = new char[] { ',', '"', '\n', '\r' };

      /** <summary>Try read a csv row from a Reader.  May consume multiple lines.  Linebreaks in cells will become \n</summary>
       * <param name="source">Reader to get line data from.</param>
       * <param name="row">Cell data enumeration (forward-only), or null if no more rows.</param>
       * <param name="quoteBuffer">Optional buffer for more efficient quote parsing, must not be shared across thread.</param>
       * <returns>True on success, false on no more rows.</returns>
       * <see cref="StreamReader.ReadLine"/> */
      public static bool TryReadCsvRow ( this TextReader source, out IEnumerable<string> row, StringBuilder quoteBuffer = null )
         => ( row = ReadCsvRow( source, quoteBuffer ) ) != null;

      /** <summary>Read a csv row from a Reader.  May consume multiple lines.  Linebreaks in cells will become \n</summary>
       * <param name="source">Reader to get line data from.</param>
       * <param name="quoteBuffer">Optional buffer for more efficient quote parsing, must not be shared across thread.</param>
       * <returns>Cell data enumeration (forward-only), or null if no more rows.</returns>
       * <see cref="StreamReader.ReadLine"/> */
      public static IEnumerable<string> ReadCsvRow ( this TextReader source, StringBuilder quoteBuffer = null ) {
         var line = source.ReadLine();
         return line == null ? null : ReadCsvCells( source, line, quoteBuffer );
      }

      private static IEnumerable<string> ReadCsvCells ( TextReader source, string line, StringBuilder buf ) {
         for ( var pos = 0 ; line?.Length >= pos ; ) yield return ReadCsvCell( source, ref line, ref pos, ref buf );
      }

      private static string ReadCsvCell ( TextReader source, ref string line, ref int pos, ref StringBuilder buf ) {
         var len = line.Length;
         if ( pos >= len ) { pos = len + 1; return ""; } // End of line.
         if ( line[ pos ] != '"' ) { // Unquoted cell.
            int end = line.IndexOf( ',', pos ), head = pos;
            if ( end < 0 ) { pos = len + 1; return line.Substring( head ); } // Last cell.
            if ( end == pos ) { pos++; return ""; } // Empty cell.
            pos = end + 1; // Normal Cell.
            return line.Substring( head, end - head );
         }
         if ( buf == null ) buf = new StringBuilder(); else buf.Length = 0;
         var start = ++pos; // Drop opening quote.
         while ( true ) {
            int end = pos < len ? line.IndexOf( '"', pos ) : -1, next = end + 1;
            if ( end < 0 ) { // End of line.  Add to buffer and read next line.
               buf.Append( line, start, len - start );
               if ( ( line = source.ReadLine() ) == null ) return buf.ToString();
               buf.Append( '\n' );
               start = pos = 0; len = line.Length;
            } else if ( next == len || line[ next ] == ',' ) { // End of cell.
               pos = end + 2;
               return buf.Append( line, start, end - start ).ToString();
            } else if ( line[ next ] == '"' ) { // Two double quotes.
               buf.Append( line, start, end - start + 1 );
               pos = start = end + 2;
            } else // One double quote not followed by EOL or comma.
               pos++;
         }
      }
      #endif

      /* Dump unity components to log. *
      public static void DumpComponents ( UnityEngine.GameObject e ) => DumpComponents( ( msg, arg ) => ModComponent.Info( msg, arg ), e );
      public static void DumpComponents ( Action< object, object[] > output, UnityEngine.GameObject e ) => DumpComponents( output, "", new HashSet<object>(), e );
      internal static void DumpComponents ( Action< object, object[] > output, string prefix, HashSet<object> logged, UnityEngine.GameObject e ) {
         if ( prefix.Length > 12 ) return;
         if ( e == null || logged.Contains( e ) ) return;
         logged.Add( e );
         Dump( output, "{0}- '{1}'{2} {3}{4}{5}{6} :{7}", prefix, e.name, ToTag( e.tag ), FindText( e ), TypeName( e ),
            e.activeSelf ? "" : " (Inactive)", e.layer == 0 ? "" : $" Layer {e.layer}", ToString( e.GetComponent<UnityEngine.Transform>() ) );
         if ( prefix.Length <= 6 )
            foreach ( var c in e.GetComponents<UnityEngine.Component>() ) try {
               var typeName = TypeName( c );
               if ( c is UnityEngine.Transform cRect ) ;
               else if ( c is UnityEngine.UI.Text txt ) Dump( output, "{0}...{1} {2} {3} {4}", prefix, typeName, txt.font, txt.fontSize, txt.text );
               else if ( c is UnityEngine.UI.Image img ) Dump( output, "{0}...{1} {2} {3}", prefix, typeName, img.sprite?.name ?? img.mainTexture?.name, img.type );
               //else if ( c is I2.Loc.Localize loc ) Dump( output, "{0}...{1} {2}", prefix, typeName, loc.mTerm );
               else if ( c is UnityEngine.UI.LayoutGroup layout ) Dump( output, "{0}...{1} Padding {2}", prefix, typeName, layout.padding );
               else Dump( output, "{0}...{1}", prefix, typeName );
            } catch ( Exception ) { }
         for ( int i = 0 ; i < e.transform.childCount ; i++ )
            DumpComponents( output, prefix + "  ", logged, e.transform?.GetChild( i )?.gameObject );
      }
      private static void Dump ( Action< object, object[] > output, object msg, params object[] augs ) => output( msg, augs );
      private static string TypeName ( object c ) => c?.GetType().FullName.Replace( "UnityEngine.", "UE." ).Replace( "UE.GameObject", "" );
      private static string ToTag ( string tag ) => "Untagged".Equals( tag ) ? "" : $":{tag}";
      private static string FindText ( UnityEngine.GameObject obj ) { var text = obj.GetComponent< UnityEngine.UI.Text >()?.text; return text == null ? "" : $"\"{text}\" "; }
      private static string ToString ( UnityEngine.Transform t ) {
         if ( t == null ) return "";
         var result = string.Format( "Pos {0} Scale {1} Rotate {2}", t.localPosition, t.localScale, t.localRotation );
         return " " + result.Replace( ".0,", "," ).Replace( ".0)", ")" ).Replace( "Pos (0, 0, 0)", "" ).Replace( "Scale (1, 1, 1)", "" ).Replace( "Rotate (0, 0, 0, 1)", "" ).Trim();
      }
      /**/ /* Dump object type and fields to log *
      public static void DumpObject ( Object e ) => ModComponent.Info( new StringBuilder().AddObj( e, new List< object >(), 3 ) );
      private static StringBuilder AddObj ( this StringBuilder str, Object e, List< object > refs, int depth ) {
         if ( e == null ) return str.Append( "null" );
         var t = e.GetType(); if ( t.IsPrimitive || t.IsEnum ) return str.Append( e );
         if ( e is string || e is StringBuilder ) return str.Append( '"' ).Append( e.ToString().Replace( "\"", "\"\"" ) ).Append( '"' );
         var i = refs.IndexOf( e ); if ( i >= 0 ) return str.Append( '#' ).Append( i.ToString( "X" ) );
         str.Append( "{#" ).Append( refs.Count.ToString( "X" ) ); refs.Add( e );
         while ( t != null ) try { var len = str.Length;
            str.Append( "[" ).Append( t.Name ).Append( "]" );
            if ( depth == 0 ) return str.Append( "...}" );
            if ( t.IsArray ) {
               if ( e is object[] ary ) { foreach ( var o in ary ) str.AddObj( o, refs, depth - 1 ).Append( ',' ); break; }
               if ( e is bool [] oa ) { str.AddAry( oa ); break; } if ( e is byte[] ba ) { str.AddAry( ba ); break; } if ( e is char[] ca ) { str.AddAry( ca ); break; }
               if ( e is short[] sa ) { str.AddAry( sa ); break; } if ( e is int [] ia ) { str.AddAry( ia ); break; } if ( e is long[] la ) { str.AddAry( la ); break; }
               if ( e is float[] fa ) { str.AddAry( fa ); break; } if ( e is double[] da ) { str.AddAry( da ); break; } if ( e is decimal[] ea ) { str.AddAry( ea ); break; }
            }
            foreach ( var f in t.GetFields( Public | NonPublic | Static | Instance | DeclaredOnly ) )
               if ( ! f.IsStatic || refs.First( f.DeclaringType.IsInstanceOfType ) == e )
                  str.Append( f.Name ).Append( ':' ).AddObj( f.GetValue( e ), refs, depth - 1 ).Append( ',' );
            if ( str[ str.Length - 1 ] != ',' && t != e.GetType() ) str.Length = len; // Remove empty super class.
         } catch ( Exception x ) { str.Append( $"({x})" ); } finally { t = t.BaseType; }
         if ( str[ str.Length - 1 ] == ',' ) str.Length--;
         return str.Append( '}' ); // Well, if you can read the output, this wall of code should be a piece of cake.
      }
      private static StringBuilder AddAry<T> ( this StringBuilder b, T[] a ) { foreach ( var e in a ) b.Append( e ).Append( ',' ); return b; }
      /**/

      #if DotNet35
      public static StringBuilder Clear ( this StringBuilder str ) { str.Length = 0; return str; }
      public static T GetCustomAttribute < T > ( this MemberInfo me ) where T: Attribute => me.GetCustomAttributes( typeof( T ), false ).FirstOrDefault() as T;
      public static bool IsBlank ( string me ) => me == null || me.Trim().Length == 0;
      public static IEnumerable< string > ReadLines ( string path ) { using ( var sr = new StreamReader( path ) ) while ( ! sr.EndOfStream ) yield return sr.ReadLine(); } 
      #else
      public static bool IsBlank ( string me ) => string.IsNullOrWhiteSpace( me );
      public static IEnumerable< string > ReadLines ( string path ) => File.ReadLines( path );
      #endif
   }

   #if ! NoConfig
   public abstract class BaseConfig  : ModComponent { // Abstract code to load and save simple config object to text-based file.  By default only process public instant fields, may be filtered by attributes.
      protected virtual string GetFileExtension () => ".conf";
      public virtual string GetDefaultPath () { lock( sync ) return Path.Combine( AppDataDir, ModName + GetFileExtension() ); }
      protected virtual bool OnLoading ( string from ) => true;
      protected virtual void OnLoad ( string from ) { }
      protected virtual bool OnSaving ( string to ) => true;
      protected virtual void OnSave ( string to ) { }

      public void Load () => Load( this );
      public void Load ( string path ) => Load( this, path );
      public void Load ( object subject ) => Load( subject, GetDefaultPath() );
      public void Load < T > ( out T subject ) where T : new() => Load( subject = new T() );
      public void Load < T > ( out T subject, string path ) where T : new() => Load( subject = new T(), path );
      public virtual void Load ( object subject, string path ) { try {
         var conf = subject as BaseConfig;
         if ( conf?.OnLoading( path ) == false ) return;
         if ( ! File.Exists( path ) ) {
            Save( subject, path );
         } else {
            Info( "Loading {0} into {1}", path, subject.GetType().FullName );
            _ReadFile( subject, path );
         }
         conf?.OnLoad( path );
         foreach ( var prop in GetType().GetFields() ) Info( "Config {0} = {1}", prop.Name, prop.GetValue( this ) );
      } catch ( Exception ex ) { Warn( ex ); } }

      protected abstract void _ReadFile ( object subject, string path );
      protected virtual bool _ReadField ( object subject, string name, out FieldInfo field ) {
         field = subject.GetType().GetField( name );
         if ( field == null ) Warn( "Unknown field: {0}", name ); // Legacy fields are expected to be kept in config class as [Obsolete].
         return field != null && ! field.IsStatic && ! field.IsInitOnly && ! field.IsNotSerialized;
      }
      protected virtual void _SetField ( object subject, FieldInfo f, string val ) {
         if ( ModHelpers.TryParse( f.FieldType, val, out object parsed, Logger ) ) f.SetValue( subject, parsed );
      }

      public void Save () => Save( this );
      public void Save ( string path ) => Save( this, path );
      public void Save ( object subject ) => Save( subject, GetDefaultPath() );
      public virtual void Save ( object subject, string path ) { try {
         if ( subject == null ) { File.Delete( path ); return; }
         var conf = subject as BaseConfig;
         if ( conf?.OnSaving( path ) == false ) return;
         var type = subject.GetType();
         Info( "Writing {0} from {1}", path, type.FullName );
         using ( TextWriter tw = File.CreateText( path ) ) {
            _WriteData( tw, subject, type, subject, _GetComments( type ) );
            foreach ( var f in _ListFields( subject ) )
               _WriteData( tw, subject, f, f.GetValue( subject ), _GetComments( f ) );
            _WriteData( tw, subject, type, subject, null );
         }
         Fine( "{0} bytes written", (Func<string>) ( () => new FileInfo( path ).Length.ToString() ) );
         conf?.OnSave( path );
      } catch ( Exception ex ) { Warn( "Cannot create config file" ); Warn( ex ); } }

      protected virtual IEnumerable< string > _GetComments ( MemberInfo mem )
         => mem.GetCustomAttributes( true ).OfType< ConfigAttribute >().Where( e => ! ModHelpers.IsBlank( e?.Comment ) ).Select( e => e.Comment );

      protected virtual IEnumerable< FieldInfo > _ListFields ( object subject ) {
         var fields = subject.GetType().GetFields()
            .Where( e => ! e.IsStatic && ! e.IsInitOnly && ! e.IsNotSerialized && e.GetCustomAttribute<ObsoleteAttribute>() == null );
         bool HasConfig ( FieldInfo f ) => f.GetCustomAttributes( typeof( ConfigAttribute ), false ).Length > 0;
         if ( fields.Any( HasConfig ) ) fields = fields.Where( HasConfig ).ToArray(); // If any field has ConfigAttribute, write only these fields.
         return fields;
      }
      /* Called before writing a type (target is Type && comment != ""), when writing a field, and after writing a type (target is Type && comment = null) */
      protected abstract void _WriteData ( TextWriter f, object subject, MemberInfo target, object value, IEnumerable< string > comment );
   }

   #if ! NoCsv
   public class CsvConfig : BaseConfig { // Load and save CSV to and from a config object.
      protected override string GetFileExtension () => ".csv";
      protected override void _ReadFile ( object subject, string path ) {
         var rowCount = 0;
         var buf = new StringBuilder();
         using ( var reader = new StreamReader( path, true ) ) while ( reader.TryReadCsvRow( out var row, buf ) ) {
            var cells = row.ToArray();
            if ( ++rowCount == 1 && cells.Length == 3 && cells[0] == "Config" && cells[1] == "Value" ) continue;
            if ( cells.Length < 2 || ModHelpers.IsBlank( cells[0] ) || ! _ReadField( subject, cells[0].Trim(), out var field ) ) continue;
            _SetField( subject, field, cells[1] ?? "" );
         }
      }
      protected override void _WriteData ( TextWriter f, object subject, MemberInfo target, object value, IEnumerable< string > comments ) {
         var comment = comments?.Any() == true ? string.Join( " ", comments.ToArray() ) : null;
         if ( target is Type ) f.WriteCsvLine( "Config", "Value", comment ?? "Comment" );
         else f.WriteCsvLine( target.Name, value, comment ?? "" );
      }
   }
   #endif

   public class IniConfig : BaseConfig { // Load and save INI to and from a config object.  Same field handling as CsvConfig.
      protected override string GetFileExtension () => ".ini";
      protected override void _ReadFile ( object subject, string path ) {
         foreach ( var line in File.ReadAllLines( path ) ) {
            var split = line.Split( new char[]{ '=' }, 2 );
            if ( split.Length != 2 || line.StartsWith( ";" ) || ! _ReadField( subject, split[ 0 ].Trim(), out var field ) ) continue;
            var val = split[1].Trim();
            if ( val.Length > 1 && val.StartsWith( "\"" ) && val.EndsWith( "\"" ) ) val = val.Substring( 1, val.Length - 2 );
            _SetField( subject, field, val );
         }
      }
      protected override void _WriteData ( TextWriter f, object subject, MemberInfo target, object value, IEnumerable< string > comments ) {
         if ( comments != null )
            foreach ( var comment in comments )
               f.Write( comment.Substring( 0, 1 ).IndexOfAny( new char[]{ '[', ';', '\r', '\n' } ) != 0 ? $"; {comment}\r\n" : $"{comment}\r\n" );
         if ( target is Type ) return;
         if ( value != null ) {
            var txt = value.ToString();
            if ( txt.Trim() != txt ) txt = "\"" + txt + "\"";
            value = txt;
         }
         f.Write( $"{target.Name} = {value}\r\n" );
      }
   }

   [ AttributeUsage( AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true ) ]
   public class ConfigAttribute : Attribute { // Slap this on config attributes for auto-doc.
      public ConfigAttribute () {}
      public ConfigAttribute ( string comment ) { Comment = comment; }
      public string Comment;
   }
   #endif

   #if ! NoPatch
   public class Patcher : ModComponent { // Patch classes may inherit from this class for manual patching.  You can still use Harmony.PatchAll, of course.
      public Harmony harmony { get; private set; }

      public class ModPatch {
         public readonly Harmony harmony;
         public readonly MethodBase original;
         public ModPatch ( Harmony patcher, MethodBase orig ) { harmony = patcher; original = orig; }
         public HarmonyMethod prefix, postfix, transpiler;
         public void Unpatch ( HarmonyPatchType type = All ) { lock ( sync ) {
            if ( prefix     != null && ( type == All || type == Prefix     ) ) { harmony.Unpatch( original, prefix.method     ); prefix     = null; }
            if ( postfix    != null && ( type == All || type == Postfix    ) ) { harmony.Unpatch( original, postfix.method    ); postfix    = null; }
            if ( transpiler != null && ( type == All || type == Transpiler ) ) { harmony.Unpatch( original, transpiler.method ); transpiler = null; }
         } }
      };

      protected virtual string GetHarmonyId () => ModName ?? Assembly.GetExecutingAssembly().CodeBase;

      private ModPatch DoPatch ( MethodBase method, string prefix = null, string postfix = null, string transpiler = null ) {
         lock ( sync ) if ( harmony == null ) harmony = new Harmony( GetHarmonyId() );
         Fine( "Patching {0} {1} | Pre: {2} | Post: {3} | Trans: {4}", method.DeclaringType, method, prefix, postfix, transpiler );
         var patch = new ModPatch( harmony, method ) { prefix = ToHarmony( prefix ), postfix = ToHarmony( postfix ), transpiler = ToHarmony( transpiler ) };
         harmony.Patch( method, patch.prefix, patch.postfix, patch.transpiler );
         return patch;
      }

      protected ModPatch Patch ( Type type, string method, string prefix = null, string postfix = null, string transpiler = null ) { try {
         return DoPatch( type.Method( method ), prefix, postfix, transpiler );
      } catch ( Exception x ) {
         Warn( "Could not patch {0} {1} | Pre: {2} | Post: {3} | Trans: {4}\n{5}", type, method, prefix, postfix, transpiler, x );
         return null;
      } }
      protected ModPatch Patch ( MethodBase method, string prefix = null, string postfix = null, string transpiler = null ) { try {
         return DoPatch( method, prefix, postfix, transpiler );
      } catch ( Exception x ) {
         Warn( "Could not patch {0} {1} | Pre: {2} | Post: {3} | Trans: {4}\n{5}", method?.DeclaringType, method?.Name, prefix, postfix, transpiler, x );
         return null;
      } }

      internal virtual void UnpatchAll () {
         lock ( sync ) if ( harmony == null ) return;
         var m = typeof( Harmony ).Method( "UnpatchAll", typeof( string ) ) ?? typeof( Harmony ).Method( "UnpatchId", typeof( string ) );
         if ( m == null ) return;
         Info( "Unpatching all." );
         m.Run( harmony, harmony.Id );
      }
      internal MethodInfo UnpatchAll ( MethodInfo orig ) {
         if ( orig == null ) return null;
         lock ( sync ) if ( harmony == null ) return orig;
         Info( "Unpatching {0}", orig );
         harmony.Unpatch( orig, All, harmony.Id );
         return null;
      }

      protected HarmonyMethod ToHarmony ( string name ) {
         if ( ModHelpers.IsBlank( name ) ) return null;
         return new HarmonyMethod( GetType().GetMethod( name, Public | NonPublic | Static ) ?? throw new NullReferenceException( $"static method {name} not found" ) );
      }
   }
   #endif

   #if ! NoLog
   // Thread safe logger.  Buffer and write in background thread unless interval is set to 0.
   // Common usages: Log an Exception (will ignore duplicates), Log a formatted string with params, Log multiple objects (in one call and on one line).
   public class ZyLogger {
      private TraceLevel _LogLevel = TraceLevel.Info;
      public TraceLevel LogLevel {
         get { lock ( buffer ) return _LogLevel; } // ReaderWriterLockSlim is tempting, but expected use case is 1 thread logging + 1 thread flushing.
         set { lock ( buffer ) {
                  if ( _LogLevel == value ) return;
                  _LogLevel = value;
                  if ( value == Off ) { flushTimer?.Stop(); buffer.Clear(); }
                  else flushTimer?.Start(); }  } }
      private string _TimeFormat = "HH:mm:ss.fff ";
      public string TimeFormat { get { lock ( buffer ) return _TimeFormat; } set { DateTime.Now.ToString( value ); lock ( buffer ) _TimeFormat = value; } }
      public readonly uint FlushInterval = 2; // Seconds.  0 to write and flush every line, reliable but way slower.
      public readonly string LogPath;

      private readonly List< string > buffer = new List<string>();
      private readonly System.Timers.Timer flushTimer;

      public ZyLogger ( string path, uint? interval = null ) { new FileInfo( path ); try {
         try { File.Delete( LogPath = path ); } catch ( IOException ) { }
         try { LoadLogOptions( path, ref FlushInterval ); } catch ( Exception ) { }
         if ( ( FlushInterval = Math.Min( interval ?? FlushInterval, 60 ) ) > 0 ) {
            flushTimer = new System.Timers.Timer( FlushInterval * 1000 ){ AutoReset = true };
            flushTimer.Elapsed += ( _, __ ) => Flush();
            AppDomain.CurrentDomain.ProcessExit += Terminate;
         }
         if ( _LogLevel == Off ) { buffer.Clear(); return; }
         buffer.Insert( 0, $"{DateTime.Now:u} {ModComponent.ModName} initiated, log level {_LogLevel}, " + ( FlushInterval > 0 ? $"refresh every {FlushInterval}s." : "no buffer." ) );
         Flush();
         flushTimer?.Start();
      } catch ( Exception ) { } }

      protected virtual void LoadLogOptions ( string path, ref uint flushInterval ) {
         var conf = Path.Combine( Path.GetDirectoryName( path ), Path.GetFileNameWithoutExtension( path ) + "-log.conf" );
         buffer.Add( $"Logging controlled by {conf}.  First line is log level (Off/Error/Warn/Info/Verbose).  Second line is write interval in seconds, 0 to 60, default 2." );
         if ( ! File.Exists( conf ) ) return;
         using ( var r = new StreamReader( conf ) ) {
            if ( TryParseLogLevel( r.ReadLine(), out var level ) ) LogLevel = level;
            if ( uint.TryParse( r.ReadLine(), out var i ) ) flushInterval = i;
         }
      }

      public static bool TryParseLogLevel ( string line, out TraceLevel level ) {
         level = TraceLevel.Off;
         if ( ! string.IsNullOrEmpty( line ) )
            switch ( line.ToUpperInvariant()[0] ) {
               case 'O' : return true;
               case 'E' : level = TraceLevel.Error; return true;
               case 'W' : level = TraceLevel.Warning; return true;
               case 'I' : level = TraceLevel.Info; return true;
               case 'V' : case 'F' : level = TraceLevel.Verbose; return true;
            }
         return false;
      }

      public void Flush () { try {
         string[] buf;
         lock ( buffer ) { if ( buffer.Count == 0 || _LogLevel == Off ) return; buf = buffer.ToArray(); buffer.Clear(); }
         using ( TextWriter f = File.AppendText( LogPath ) ) foreach ( var line in buf ) f.WriteLine( line );
      } catch ( Exception ) { } }

      private void Terminate ( object _, EventArgs __ ) { Flush(); LogLevel = Off; AppDomain.CurrentDomain.ProcessExit -= Terminate; }

      private readonly HashSet< int > knownErrors = new HashSet< int >(); // Known exceptions are ignored.  Modding is risky.

      public void Write ( TraceLevel level, object msg, params object[] arg ) {
         string line;
         lock ( buffer ) { if ( level > _LogLevel ) return; line = _TimeFormat; }
         try {
            if ( ( line = Format( level, line, msg, arg ) ) == null ) return;
         } catch ( Exception e ) { // ToString error, time format error, stacktrace error...
            if ( msg is Exception ex ) line = ex.GetType() + ": " + ex.Message;
            else { Write( TraceLevel.Warning, e ); if ( msg is string txt ) line = txt; else return; }
         }
         lock ( buffer ) buffer.Add( line );
         if ( level == TraceLevel.Error || FlushInterval == 0 ) Flush();
      }

      protected virtual string Format ( TraceLevel level, string timeFormat, object msg, object[] arg )
         => DefaultFormatter( level, knownErrors, timeFormat, msg, arg );

      public static string DefaultFormatter ( TraceLevel level, ICollection< int > knownErrors, string timeFormat, object msg, params object[] arg ) {
         string tag = "INFO ";
         switch ( level ) {
            case TraceLevel.Off : return null;
            case TraceLevel.Error   : tag = "ERROR "; break;
            case TraceLevel.Warning : tag = "WARN " ; break;
            case TraceLevel.Verbose : tag = "FINE " ; break;
         }
         if ( ! string.IsNullOrEmpty( timeFormat ) ) tag = DateTime.Now.ToString( timeFormat ) + tag;
         msg = DefaultFormatter( knownErrors, msg, arg );
         return msg == null ? null : tag + msg;
      }

      public static string DefaultFormatter ( ICollection< int > knownErrors, object msg, params object[] arg ) {
         if ( arg != null ) for ( var i = arg.Length - 1 ; i >= 0 ; i-- ) if ( arg[i] is Func<string> f ) arg[i] = f();
         if ( msg is string txt && txt.Contains( '{' ) && arg?.Length > 0 ) msg = string.Format( msg.ToString(), arg );
         else if ( msg is Exception ) {
            var str = msg.ToString();
            if ( knownErrors != null ) lock ( knownErrors ) {
               var hash = knownErrors.GetHashCode();
               if ( knownErrors.Contains( hash ) ) return null;
               knownErrors.Add( hash );
            }
            msg = str;
         }
         #if DotNet35
         else if ( arg?.Length > 0 ) msg = string.Join( ", ", new object[] { msg }.Union( arg ).Select( e => e?.ToString() ?? "null" ).ToArray() );
         #else
         else if ( arg?.Length > 0 ) msg = string.Join( ", ", new object[] { msg }.Union( arg ).Select( e => e?.ToString() ?? "null" ) );
         #endif
         else msg = msg?.ToString();
         return msg?.ToString() ?? "null";
      }
   }
   #endif
}