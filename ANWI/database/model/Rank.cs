﻿using System.Data.SQLite;

namespace ANWI.Database.Model
{
    /// <summary>
    /// Represents a row of the Rank table.
    /// </summary>

    public class Rank
    {
        #region Model

        public int id;
        public string name;
        public string abrv;
        public string icon;
        public int ordering;

        private Rank(int id, string name, string abrv, string icon, int ordering)
        {
            this.id = id;
            this.name = name;
            this.abrv = abrv;
            this.icon = icon;
            this.ordering = ordering;
        }

        #endregion

        #region Instance-Members



        #endregion

        #region Class-Members

        public static Rank Factory()
        {
            Rank result = new Rank(
                id: -1,
                name: "",
                abrv: "",
                icon: "",
                ordering: 0
            );
            return result;
        }

        public static Rank Factory(int id, string name, string abrv, string icon, int ordering)
        {
            Rank result = new Rank(
                id: id,
                name: name,
                abrv: abrv,
                icon: icon,
                ordering: ordering
            );
            return result;
        }

        public static Rank Factory(SQLiteDataReader reader)
        {
            Rank result = new Rank(
                id: (int)reader["id"],
                name: (string)reader["name"],
                abrv: (string)reader["abrv"],
                icon: (string)reader["icon"],
                ordering: (int)reader["order"]
            );
            return result;
        }

        #endregion
    }
}