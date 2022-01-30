using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using static System.Diagnostics.TraceLevel;
using static System.Reflection.BindingFlags;
using static HarmonyLib.HarmonyPatchType;

// Sheepy's "Universal" skeleton mod and tools.  No depency other than Harmony2 / HarmonyX.
// Bootstrap, Background Logging, Roundtrip Config, Reflection, Manual Patcher with Unpatch. Reasonably well unit tested.
namespace ZyMod {
   public abstract class RootMod {
      protected static readonly object sync = new object();
      private static RootMod instance;
      public static ZyLogger Log { get; private set; }
      internal static string ModName { get { lock ( sync ) return instance?.GetModName() ?? "ZyMod"; } }

      protected virtual bool IgnoreAssembly ( Assembly asm ) => asm is AssemblyBuilder || asm.FullName.StartsWith( "DMDASM." ) || asm.FullName.StartsWith( "HarmonyDTFAssembly" );
      protected virtual bool IsTargetAssembly ( Assembly asm ) => asm.GetName().Name == "Assembly-CSharp"; // If overrode, OnGameAssemblyLoaded may be called mutliple times

      public void Initialize () {
         lock ( sync ) { if ( instance != null ) { Log?.Warn( "Mod already initialized" ); return; } instance = this; }
         try {
            Log = new ZyLogger( Path.Combine( AppDataDir, ModName + ".log" ) );
            AppDomain.CurrentDomain.UnhandledException += ( _, evt ) => Log?.Error( evt.ExceptionObject );
            AppDomain.CurrentDomain.AssemblyResolve += ( _, evt ) => { Log?.Fine( "Resolving {0}", evt.Name ); return null; };
            AppDomain.CurrentDomain.AssemblyLoad += ( _, evt ) => AsmLoaded( evt.LoadedAssembly );;
            foreach ( var asm in AppDomain.CurrentDomain.GetAssemblies().ToArray() ) AsmLoaded( asm );
            Log.Info( "Mod Initiated" );
         } catch ( Exception ex ) {
            Log.Error( ex.ToString() );
         }
      }

      private void AsmLoaded ( Assembly asm ) {
         if ( IgnoreAssembly( asm ) ) return;
         Log.Fine( "DLL {0}, {1}", asm.FullName, asm.CodeBase );
         if ( IsTargetAssembly( asm ) ) GameLoaded( asm );
      }

      private void GameLoaded ( Assembly asm ) { try {
         Log.Info( "Target assembly loaded." );
         OnGameAssemblyLoaded( asm );
         var patches = new Harmony( ModName ).GetPatchedMethods().Select( e => Harmony.GetPatchInfo( e ) );
         Log.Info( "Bootstrap complete." + ( patches.Any() ? "  Patched {0} methods with {1} patches." : "" ),
            patches.Count(), patches.Sum( e => e.Prefixes.Count + e.Postfixes.Count + e.Transpilers.Count ) );
      } catch ( Exception ex ) { Log.Error( ex ); } }

      private static string _AppDataDir;
      public static string AppDataDir { get {
         if ( _AppDataDir != null ) return _AppDataDir;
         lock ( sync ) { if ( instance == null ) return ""; _AppDataDir = instance.GetAppDataDir(); }
         if ( string.IsNullOrEmpty( _AppDataDir ) ) return "";
         try {
            if ( ! Directory.Exists( _AppDataDir ) ) {
               Directory.CreateDirectory( _AppDataDir );
               if ( ! Directory.Exists( _AppDataDir ) ) _AppDataDir = "";
            }
         } catch ( Exception ) { _AppDataDir = ""; }
         return _AppDataDir;
      } }

      // Override / Implement these to change mod name, log dir, what to do on Assembly-CSharp, and where patches are located by Modder.
      protected virtual string GetModName () => GetType().Name;
      protected abstract string GetAppDataDir (); // Called once on start.  At most once per thread.  Result will be cached.
      protected abstract void OnGameAssemblyLoaded ( Assembly game ); // Put all the actions here.
   }

   public static class ModHelpers { // Assorted helpers
      public static void Err ( object msg ) => Error( msg );
      public static T Err < T > ( object msg, T val ) { Error( msg ); return val; }
      public static void Error ( object msg, params object[] arg ) => RootMod.Log?.Error( msg, arg );
      public static void Warn  ( object msg, params object[] arg ) => RootMod.Log?.Warn ( msg, arg );
      public static void Info  ( object msg, params object[] arg ) => RootMod.Log?.Info ( msg, arg );
      public static void Fine  ( object msg, params object[] arg ) => RootMod.Log?.Fine ( msg, arg );
      public static bool Non0 ( float val ) => val != 0 && ! float.IsNaN( val ) && ! float.IsInfinity( val );
      public static bool IsFound ( string path ) { if ( File.Exists( path ) ) return true; Warn( "Not Found: {0}", path ); return false; }
      public static bool IsFound ( string path, out string found ) { found = path; return IsFound( path ); }

      public static string ModPath => new Uri( Assembly.GetExecutingAssembly().CodeBase ).LocalPath;
      public static string ModDir => Path.GetDirectoryName( ModPath );

      public static IEnumerable< MethodInfo > Methods ( this Type type ) => type.GetMethods( Public | NonPublic | Instance | Static ).Where( e => ! e.IsAbstract );
      public static IEnumerable< MethodInfo > Methods ( this Type type, string name ) => type.Methods().Where( e => e.Name == name );

      public static MethodInfo Method ( this Type type, string name ) => type?.GetMethod( name, Public | NonPublic | Instance | Static );
      public static MethodInfo Method ( this Type type, string name, params Type[] types ) => type?.GetMethod( name, Public | NonPublic | Instance | Static, null, types ?? Type.EmptyTypes, null );
      public static MethodInfo TryMethod ( this Type type, string name ) { try { return Method( type, name ); } catch ( Exception ) { return null; } }
      public static MethodInfo TryMethod ( this Type type, string name, params Type[] types ) { try { return Method( type, name, types ); } catch ( Exception ) { return null; } }
      public static object MethodInvoke ( this object target, string name, params object[] args ) => Method( target.GetType(), name ).Invoke( target, args );
      public static object TryInvoke ( this object target, string name, params object[] args ) { try {  return MethodInvoke( target, name, args ); } catch ( Exception x ) { return x; } }
      public static FieldInfo  Field ( this Type type, string name ) => type?.GetField( name, Public | NonPublic | Instance | Static );
      public static PropertyInfo Property ( this Type type, string name ) => type?.GetProperty( name, Public | NonPublic | Instance | Static );

      public static bool TryParse ( Type valueType, string val, out object parsed, bool logWarnings = true ) { parsed = null; try {
         if ( valueType == typeof( string ) ) { parsed = val; return true; }
         if ( IsBlank( val ) || val == "null" ) return ! ( valueType.IsValueType || valueType.IsEnum );
         switch ( valueType.FullName ) {
            case "System.SByte"   : if ( SByte .TryParse( val, out sbyte  bval ) ) parsed = bval; break;
            case "System.Int16"   : if ( Int16 .TryParse( val, out short  sval ) ) parsed = sval; break;
            case "System.Int32"   : if ( Int32 .TryParse( val, out int    ival ) ) parsed = ival; break;
            case "System.Int64"   : if ( Int64 .TryParse( val, out long   lval ) ) parsed = lval; break;
            case "System.Byte"    : if ( Byte  .TryParse( val, out byte   Bval ) ) parsed = Bval; break;
            case "System.UInt16"  : if ( UInt16.TryParse( val, out ushort Sval ) ) parsed = Sval; break;
            case "System.UInt32"  : if ( UInt32.TryParse( val, out uint   Ival ) ) parsed = Ival; break;
            case "System.UInt64"  : if ( UInt64.TryParse( val, out ulong  Lval ) ) parsed = Lval; break;
            case "System.Single"  : if ( Single.TryParse( val, out float  fval ) ) parsed = fval; break;
            case "System.Double"  : if ( Double.TryParse( val, out double dval ) ) parsed = dval; break;
            case "System.Boolean" : switch ( val.ToLowerInvariant() ) {
                                    case "true" : case "yes" : case "1" : parsed = true ; break;
                                    case "false" : case "no" : case "0" : parsed = false; break;
                                    } break;
            case "System.DateTime" :
               if ( DateTime.TryParse( val, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime dt ) ) parsed = dt;
               break;
            default :
               if ( valueType.IsEnum ) { parsed = Enum.Parse( valueType, val ); break; }
               if ( logWarnings ) Warn( new NotImplementedException( "Unsupported field type " + valueType.FullName ) );
               break;
         }
         return parsed != null;
      } catch ( ArgumentException ) { if ( logWarnings ) Warn( "Invalid value for {0}: {1}", valueType.FullName, val ); return false; } }

      /** Write data as a csv row, and then start a new line.  Null will be written as "null". */
      public static void WriteCsvLine ( this TextWriter tw, params object[] values ) => tw.Write( new StringBuilder().AppendCsvLine( values ).Append( "\r\n" ) );

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
       * <param name="quoteBuffer">Thread-local buffer for quote parsing. If null, one will be created on demand.</param>
       * <returns>True on success, false on no more rows.</returns>
       * <see cref="StreamReader.ReadLine"/> */
      public static bool TryReadCsvRow ( this TextReader source, out IEnumerable<string> row, StringBuilder quoteBuffer = null )
         => ( row = ReadCsvRow( source, quoteBuffer ) ) != null;

      /** <summary>Read a csv row from a Reader.  May consume multiple lines.  Linebreaks in cells will become \n</summary>
       * <param name="source">Reader to get line data from.</param>
       * <param name="quoteBuffer">Thread-local buffer for quote parsing. If null, one will be created on demand.</param>
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
         if ( buf == null ) buf = new StringBuilder(); else buf.Clear();
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

      /* Dump unity components to log. *
      public static void DumpComponents ( UnityEngine.GameObject e ) => DumpComponents( Info, "", new HashSet<object>(), e );
      public static void DumpComponents ( Action< object, object[] > output, UnityEngine.GameObject e ) => DumpComponents( output, "", new HashSet<object>(), e );
      internal static void DumpComponents ( Action< object, object[] > output, string prefix, HashSet<object> logged, UnityEngine.GameObject e ) {
         if ( prefix.Length > 10 ) return;
         if ( e == null || logged.Contains( e ) ) return;
         logged.Add( e );
         Dump( output, "{0}- '{1}'{2} {3}{4}{5}{6} :{7}", prefix, e.name, ToTag( e.tag ), FindText( e ), TypeName( e ),
            e.activeSelf ? "" : " (Inactive)", e.layer == 0 ? "" : $" Layer {e.layer}", ToString( e.GetComponent<UnityEngine.Transform>() ) );
         if ( prefix.Length == 0 )
            foreach ( var c in e.GetComponents<UnityEngine.Component>() ) try {
               var typeName = TypeName( c );
               if ( c is UnityEngine.Transform cRect ) ;
               else if ( c is UnityEngine.UI.Text txt ) Dump( output, "{0}...{1} {2} {3}", prefix, typeName, txt.color, txt.text );
               else if ( c is I2.Loc.Localize loc ) Dump( output, "{0}...{1} {2}", prefix, typeName, loc.mTerm );
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

   public abstract class BaseConfig { // Abstract code to load and save simple config object to text-based file.  By default only process public instant fields, may be filtered by attributes.
      protected virtual string GetFileExtension () => ".conf";
      public virtual string GetDefaultPath () => Path.Combine( RootMod.AppDataDir, RootMod.ModName + GetFileExtension() );

      public void Load () => Load( this );
      public void Load ( string path ) => Load( this, path );
      public void Load ( object subject ) => Load( subject, GetDefaultPath() );
      public void Load < T > ( out T subject ) where T : new() => Load( subject = new T() );
      public void Load < T > ( out T subject, string path ) where T : new() => Load( subject = new T(), path );
      public virtual void Load ( object subject, string path ) { try {
         if ( ! File.Exists( path ) ) {
            Save( subject, path );
         } else {
            _Log( Info, "Loading {0} into {1}", path, new object[]{ subject.GetType().FullName } );
            _ReadFile( subject, path );
         }
         foreach ( var prop in GetType().GetFields() ) _Log( Info, "Config {0} = {1}", prop.Name, prop.GetValue( this ) );
      } catch ( Exception ex ) {  _Log( Warning, ex ); } }

      protected abstract void _ReadFile ( object subject, string path );
      protected virtual bool _ReadField ( object subject, string name, out FieldInfo field ) {
         field = subject.GetType().GetField( name );
         if ( field == null ) _Log( Warning, "Unknown field: {0}", name ); // Legacy fields are expected to be kept in config class as [Obsolete].
         return field != null && ! field.IsStatic && ! field.IsInitOnly && ! field.IsNotSerialized;
      }
      protected virtual void _SetField ( object subject, FieldInfo f, string val ) {
         if ( ModHelpers.TryParse( f.FieldType, val, out object parsed ) ) f.SetValue( subject, parsed );
      }

      public void Save () => Save( this );
      public void Save ( string path ) => Save( this, path );
      public void Save ( object subject ) => Save( subject, GetDefaultPath() );
      public virtual void Save ( object subject, string path ) { try {
         if ( subject == null ) { File.Delete( path ); return; }
         var type = subject.GetType();
         _Log( Info, "Creating {0} from {1}", path, type.FullName );
         using ( TextWriter tw = File.CreateText( path ) ) {
            var attr = type.GetCustomAttribute<ConfigAttribute>();
            var comment = ! ModHelpers.IsBlank( attr?.Comment ) ? attr.Comment : null;
            _WriteData( tw, subject, type, subject, comment );
            foreach ( var f in _ListFields( subject ) ) {
               comment = ( attr = f.GetCustomAttribute<ConfigAttribute>() ) != null && ! string.IsNullOrEmpty( attr.Comment ) ? attr.Comment : null;
               _WriteData( tw, subject, f, f.GetValue( subject ), comment );
            }
            _WriteData( tw, subject, type, subject, "" );
         }
         _Log( Verbose, "{0} bytes written", (Func<string>) ( () => new FileInfo( path ).Length.ToString() ) );
      } catch ( Exception ex ) { _Log( Warning, "Cannot create config file: {0}", ex ); } }

      protected virtual IEnumerable<FieldInfo> _ListFields ( object subject ) {
         var fields = subject.GetType().GetFields()
            .Where( e => ! e.IsStatic && ! e.IsInitOnly && ! e.IsNotSerialized && e.GetCustomAttribute<ObsoleteAttribute>() == null );
         if ( fields.Any( e => e.GetCustomAttribute<ConfigAttribute>() != null ) ) // If any field has ConfigAttribute, save only these fields.
            fields = fields.Where( e => e.GetCustomAttribute<ConfigAttribute>() != null ).ToArray();
         return fields;
      }
      protected abstract void _WriteData ( TextWriter f, object subject, MemberInfo target, object value, string comment );
      protected virtual void _Log ( TraceLevel level, object msg, params object[] arg ) => RootMod.Log?.Write( level, msg, arg );
   }

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
      protected override void _WriteData ( TextWriter f, object subject, MemberInfo target, object value, string comment ) {
         if ( target is Type ) { if ( comment != "" ) f.WriteCsvLine( "Config", "Value", comment ?? "Comment" ); }
         else f.WriteCsvLine( target.Name, value, comment ?? "" );
      }
   }

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
      protected override void _WriteData ( TextWriter f, object subject, MemberInfo target, object value, string comment ) {
         if ( ! string.IsNullOrEmpty( comment ) ) f.Write( comment.Substring( 0, 1 ).IndexOfAny( new char[]{ '[', ';', '\r', '\n' } ) != 0 ? $"; {comment}\r\n" : $"{comment}\r\n" );
         if ( target is Type ) return;
         if ( value != null ) {
            value = comment = value.ToString();
            if ( comment.Trim() != comment ) value = "\"" + comment + "\"";
         }
         f.Write( $"{target.Name} = {value}\r\n" );
      }
   }

   [ AttributeUsage( AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property ) ]
   public class ConfigAttribute : Attribute { // Slap this on config attributes for auto-doc.
      public ConfigAttribute () {}
      public ConfigAttribute ( string comment ) { Comment = comment; }
      public string Comment;
   }

   public class Patcher { // Patch classes may inherit from this class for manual patching.  You can still use Harmony.PatchAll, of course.
      protected static readonly object sync = new object();
      public Harmony harmony { get; private set; }

      public class ModPatch {
         public readonly Harmony harmony;
         public ModPatch ( Harmony patcher ) { harmony = patcher; }
         public MethodBase original; public HarmonyMethod prefix, postfix, transpiler;
         public void Unpatch ( HarmonyPatchType type = All ) { lock ( sync ) {
            if ( prefix     != null && ( type == All || type == Prefix     ) ) { harmony.Unpatch( original, prefix.method     ); prefix     = null; }
            if ( postfix    != null && ( type == All || type == Postfix    ) ) { harmony.Unpatch( original, postfix.method    ); postfix    = null; }
            if ( transpiler != null && ( type == All || type == Transpiler ) ) { harmony.Unpatch( original, transpiler.method ); transpiler = null; }
         } }
      };

      protected ModPatch Patch ( Type type, string method, string prefix = null, string postfix = null, string transpiler = null ) =>
         Patch( type.Method( method ), prefix, postfix, transpiler );
      protected ModPatch Patch ( MethodBase method, string prefix = null, string postfix = null, string transpiler = null ) {
         lock ( sync ) if ( harmony == null ) harmony = new Harmony( RootMod.ModName );
         ModHelpers.Fine( "Patching {0} {1} | Pre: {2} | Post: {3} | Trans: {4}", method.DeclaringType, method, prefix, postfix, transpiler );
         var patch = new ModPatch( harmony ) { original = method, prefix = ToHarmony( prefix ), postfix = ToHarmony( postfix ), transpiler = ToHarmony( transpiler ) };
         lock ( sync ) harmony.Patch( method, patch.prefix, patch.postfix, patch.transpiler );
         return patch;
      }

      protected ModPatch TryPatch ( Type type, string method, string prefix = null, string postfix = null, string transpiler = null ) =>
         TryPatch( type.Method( method ), prefix, postfix, transpiler );
      protected ModPatch TryPatch ( MethodBase method, string prefix = null, string postfix = null, string transpiler = null ) { try {
         return Patch( method, prefix, postfix, transpiler );
      } catch ( Exception ex ) {
         ModHelpers.Warn( "Could not patch {0} {1} | Pre: {2} | Post: {3} | Trans: {4}\n{5}", method?.DeclaringType, method?.Name, prefix, postfix, transpiler, ex );
         return null;
      } }

      protected void UnpatchAll () {
         var m = typeof( Harmony ).Method( "UnpatchAll", typeof( string ) ) ?? typeof( Harmony ).Method( "UnpatchId", typeof( string ) );
         lock ( sync ) {
            if ( harmony == null ) return;
            m?.Invoke( harmony, new object[]{ harmony.Id } );
         }
      }
      protected MethodInfo UnpatchAll ( MethodInfo orig ) { if ( orig != null ) lock ( sync ) harmony?.Unpatch( orig, All, harmony.Id ); return null; }

      protected HarmonyMethod ToHarmony ( string name ) {
         if ( ModHelpers.IsBlank( name ) ) return null;
         return new HarmonyMethod( GetType().GetMethod( name, Public | NonPublic | Static ) ?? throw new NullReferenceException( name + " not found" ) );
      }
   }

   // Thread safe logger.  Buffer and write in background thread unless interval is set to 0.
   // Common usages: Log an Exception (will ignore duplicates), Log a formatted string with params, Log multiple objects (in one call and on one line).
   public class ZyLogger {
      private TraceLevel _LogLevel = TraceLevel.Info;
      public TraceLevel LogLevel {
         get { lock ( buffer ) return _LogLevel; } // ReaderWriterLockSlim is tempting, but expected use case is 1 thread logging + 1 thread flushing.
         set { lock ( buffer ) { _LogLevel = value;
                  if ( value == Off ) { flushTimer?.Stop(); buffer.Clear(); }
                  else flushTimer?.Start(); }  } }
      private string _TimeFormat = "hh:mm:ss.fff ";
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
         buffer.Insert( 0, $"{DateTime.Now:u} {RootMod.ModName} initiated, log level {_LogLevel}, " + ( FlushInterval > 0 ? $"refresh every {FlushInterval}s." : "no buffer." ) );
         Flush();
         flushTimer?.Start();
      } catch ( Exception ) { } }

      protected virtual void LoadLogOptions ( string path, ref uint flushInterval ) {
         var conf = Path.Combine( Path.GetDirectoryName( path ), Path.GetFileNameWithoutExtension( path ) + "-log.conf" );
         buffer.Add( $"Logging controlled by {conf}.  First line is log level (Off/Error/Warn/Info/Verbose).  Second line is write interval in seconds, 0 to 60, default 2." );
         if ( ! File.Exists( conf ) ) return;
         using ( var r = new StreamReader( conf ) ) {
            var line = r.ReadLine();
            if ( line == null ) return;
            switch ( ( line.ToUpperInvariant() + "?" )[0] ) {
               case 'O' : LogLevel = TraceLevel.Off; break;
               case 'E' : LogLevel = TraceLevel.Error; break;
               case 'W' : LogLevel = TraceLevel.Warning; break;
               case 'I' : LogLevel = TraceLevel.Info; break;
               case 'V' : case 'F' : LogLevel = TraceLevel.Verbose; break;
            }
            uint i = 0;
            if ( uint.TryParse( r.ReadLine(), out i ) ) flushInterval = i;
         }
      }

      public void Error ( object msg, params object[] arg ) => Write( TraceLevel.Error, msg, arg );
      public void Warn  ( object msg, params object[] arg ) => Write( TraceLevel.Warning, msg, arg );
      public void Info  ( object msg, params object[] arg ) => Write( TraceLevel.Info, msg, arg );
      public void Fine  ( object msg, params object[] arg ) => Write( TraceLevel.Verbose, msg, arg );

      public void Flush () { try {
         string[] buf;
         lock ( buffer ) { if ( buffer.Count == 0 || _LogLevel == Off ) return; buf = buffer.ToArray(); buffer.Clear(); }
         using ( TextWriter f = File.AppendText( LogPath ) ) foreach ( var line in buf ) f.WriteLine( line );
      } catch ( Exception ) { } }

      private void Terminate ( object _, EventArgs __ ) { Flush(); LogLevel = Off; AppDomain.CurrentDomain.ProcessExit -= Terminate; }

      private readonly HashSet< string > knownErrors = new HashSet<string>(); // Known exceptions are ignored.  Modding is risky.

      public void Write ( TraceLevel level, object msg, params object[] arg ) {
         string line;
         lock ( buffer ) { if ( level > _LogLevel ) return; line = _TimeFormat; }
         try {
            if ( ( line = Format( level, line, msg, arg ) ) == null ) return;
         } catch ( Exception e ) { // ToString error, time format error, stacktrace error...
            if ( msg is Exception ex ) line = ex.GetType() + ": " + ex.Message;
            else { Warn( e ); if ( msg is string txt ) line = txt; else return; }
         }
         lock ( buffer ) buffer.Add( line );
         if ( level == TraceLevel.Error || FlushInterval == 0 ) Flush();
      }

      protected virtual string Format ( TraceLevel level, string timeFormat, object msg, object[] arg ) {
         string tag = "INFO ";
         switch ( level ) {
            case TraceLevel.Off : return null;
            case TraceLevel.Error   : tag = "ERROR "; break;
            case TraceLevel.Warning : tag = "WARN " ; break;
            case TraceLevel.Verbose : tag = "FINE " ; break;
         }
         if ( arg != null ) for ( var i = arg.Length - 1 ; i >= 0 ; i-- ) if ( arg[i] is Func<string> f ) arg[i] = f();
         if ( msg is string txt && txt.Contains( '{' ) && arg?.Length > 0 ) msg = string.Format( msg.ToString(), arg );
         else if ( msg is Exception ) { var str = msg.ToString(); lock ( knownErrors ) { if ( knownErrors.Contains( str ) ) return null; knownErrors.Add( str ); } msg = str; }
         #if DotNet35
         else if ( arg?.Length > 0 ) msg = string.Join( ", ", new object[] { msg }.Union( arg ).Select( e => e?.ToString() ?? "null" ).ToArray() );
         #else
         else if ( arg?.Length > 0 ) msg = string.Join( ", ", new object[] { msg }.Union( arg ).Select( e => e?.ToString() ?? "null" ) );
         #endif
         else msg = msg?.ToString();
         if ( ! string.IsNullOrEmpty( timeFormat ) ) tag = DateTime.Now.ToString( timeFormat ) + tag;
         return tag + ( msg?.ToString() ?? "null" );
      }
   }
}