using System;
using System.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BattleTechModLoader
{
    public class FileToIgnore : ConfigurationElement
    {
        [ConfigurationProperty("FileName", IsRequired = true)]
        public string FileName
        {
            get { return (string)this["FileName"]; }
            set { value = (string)this["FileName"]; }
        }
    }

    public class FileToIgnoreSection : ConfigurationSection
    {
        [ConfigurationProperty("FileToIgnoreSection", IsRequired = true)]
        public static FileToIgnoreSection FilesToIgnore => ConfigurationManager.GetSection("FileToIgnoreSection") as FileToIgnoreSection;

        [ConfigurationProperty("FileToIgnore", IsRequired = true)]
        public FileToIgnoreCollection Files
        {
            get { return (FileToIgnoreCollection)this["FileToIgnore"]; }
            set { this["FileToIgnore"] = value; }
        }
    }

    public class FileToIgnoreCollection : ConfigurationElementCollection
    {
        public List<FileToIgnore> All { get { return this.Cast<FileToIgnore>().ToList(); } }

        public FileToIgnore this[int index]
        {
            get
            {
                return base.BaseGet(index) as FileToIgnore;
            }
            set
            {
                if (base.BaseGet(index) != null)
                {
                    base.BaseRemoveAt(index);
                }
                this.BaseAdd(index, value);
            }
        }

        protected override ConfigurationElement CreateNewElement()
        {
            return new FileToIgnore();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((FileToIgnore)element).FileName;
        }
    }
}
