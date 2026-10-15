// TI Debug Server Scripting (DSS). No GUI keystrokes or external runtime needed.
importPackage(Packages.com.ti.debug.engine.scripting);
importPackage(Packages.com.ti.ccstudio.scripting.environment);
var env = ScriptingEnvironment.instance();
env.setScriptTimeout(120000);
var server = null, session = null, success = false, writeStarted = false;
try {
    if (arguments.length !== 3) throw new Error("ccxml, out ve yedek yolu gerekli.");
    server = env.getServer("DebugServer.1");
    server.setConfig(String(arguments[0]));
    session = server.openSession("*", "CORTEX_M4_0");
    session.target.connect();
    session.target.halt();
    var did0 = Number(session.memory.readWord(0, 0x400FE000));
    var did1 = Number(session.memory.readWord(0, 0x400FE004));
    if (((did0 >>> 16) & 255) !== 5 || ((did1 >>> 16) & 255) !== 161)
        throw new Error("Bu islemci TM4C123GH6PM degil. Flash degistirilmedi.");
    var backup = String(arguments[2]);
    if (new java.io.File(backup).exists()) throw new Error("Yedek dosyasi zaten var.");
    // DSS length is a count of 32-bit memory words for this target.
    session.memory.saveRaw(0, 0, backup, 65536, 32, false);
    if (new java.io.File(backup).length() !== 262144) throw new Error("Yedek boyutu hatali; yukleme yapilmadi.");
    print("FLASH_BACKUP_OK " + backup);
    writeStarted = true;
    session.memory.loadProgram(String(arguments[1]));
    session.target.restart();
    session.target.runAsynch();
    java.lang.Thread.sleep(1300);
    session.target.halt();
    var counterAddress = Number(session.symbol.getAddress("renk_sayaci"));
    var counter1 = Number(session.memory.readWord(0, counterAddress));
    session.target.runAsynch();
    java.lang.Thread.sleep(1000);
    session.target.halt();
    var counter2 = Number(session.memory.readWord(0, counterAddress));
    print("RGB_COUNTER " + counter1 + " -> " + counter2);
    if (counter2 <= counter1) throw new Error("RGB dongusu ilerlemiyor.");
    session.target.runAsynch();
    session.target.disconnect();
    success = true;
    print("TM4C_FLASH_AND_RUN_OK");
} catch (e) {
    print("TM4C_FLASH_ERROR " + e);
    if (session !== null && !writeStarted) {
        try { session.target.runAsynch(); session.target.disconnect(); } catch (ignored) {}
    }
} finally {
    if (session !== null) { try { session.terminate(); } catch (e) {} }
    if (server !== null) { try { server.stop(); } catch (e) {} }
}
java.lang.System.exit(success ? 0 : 1);
