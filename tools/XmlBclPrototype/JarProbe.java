import java.nio.file.*;
import java.sql.*;
import java.util.*;

// ASCII transport preserves every UTF-16 code unit, including unpaired surrogates.
public class JarProbe {
    static String decode(String text) {
        StringBuilder result = new StringBuilder();
        for (int i=0; i<text.length(); i+=4) result.append((char)Integer.parseInt(text.substring(i,i+4),16));
        return result.toString();
    }
    static String encode(String text) {
        StringBuilder result = new StringBuilder();
        for (int i=0; i<text.length(); i++) result.append(String.format("%04x",(int)text.charAt(i)));
        return result.toString();
    }
    public static void main(String[] args) throws Exception {
        try (Connection c = DriverManager.getConnection("jdbc:h2:mem:xml_bcl_prototype")) {
            Statement stat = c.createStatement();
            for (String line : Files.readAllLines(Path.of(args[0]))) {
                String[] data = line.split("\\|",2);
                try (ResultSet r = stat.executeQuery(decode(data[1]))) {
                    r.next(); String value = r.getString(1);
                    System.out.println(data[0] + (value == null ? "|NULL|" : "|OK|" + encode(value)));
                } catch (SQLException e) {
                    System.out.println(data[0] + "|ERR|" + e.getErrorCode() + "|" + encode(e.getMessage()));
                }
            }
        }
    }
}
