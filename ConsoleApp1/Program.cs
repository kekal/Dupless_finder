using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    class Program
    {
        static void Main(string[] args)
        {
            if (File.Exists("test.db3"))
            {
                File.Delete("test.db3");
            }
            using (var connection = new SQLiteConnection("Data Source=test.db3;Version=3"))
            using (var command = new SQLiteCommand("CREATE TABLE PHOTOS(ID INTEGER PRIMARY KEY AUTOINCREMENT, PHOTO BLOB)", connection))
            {
                connection.Open();
                command.ExecuteNonQuery();

                byte[] photo = new byte[] { 1, 2, 3, 4, 5 };

                command.CommandText = "INSERT INTO PHOTOS (PHOTO) VALUES (@photo)";
                command.Parameters.Add("@photo", DbType.Binary, 20).Value = photo;
                command.ExecuteNonQuery();

                command.CommandText = "SELECT PHOTO FROM PHOTOS WHERE ID = 1";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byte[] buffer = GetBytes(reader);
                    }
                }

            }
        }

        static byte[] GetBytes(SQLiteDataReader reader)
        {
            const int CHUNK_SIZE = 2 * 1024;
            byte[] buffer = new byte[CHUNK_SIZE];
            long bytesRead;
            long fieldOffset = 0;
            using (MemoryStream stream = new MemoryStream())
            {
                while ((bytesRead = reader.GetBytes(0, fieldOffset, buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, (int)bytesRead);
                    fieldOffset += bytesRead;
                }
                return stream.ToArray();
            }
        }
    }
}
