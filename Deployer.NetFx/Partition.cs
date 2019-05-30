﻿using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using ByteSizeLib;
using Deployer.FileSystem;
using Deployer.FileSystem.Gpt;
using Serilog;

namespace Deployer.NetFx
{
    public class Partition : IPartition
    {
        public Partition(IDisk disk)
        {
            Disk = disk;
        }

        public IDisk Disk { get; }

        public Guid Guid { get; set; }
        public PartitionType PartitionType { get; set; }
        public string Root { get; set; }
        public string Name { get; set; }
        public uint Number { get; set; }
        public string UniqueId { get; set; }

        public async Task<char> AssignDriveLetter()
        {
            var part = await this.GetPsPartition();
            var letter = GetFreeDriveLetter();
            await PowerShellMixin.ExecuteCommand("Set-Partition",
                ("InputObject", part),
                ("NewDriveLetter", letter)
            );

            Root = PathExtensions.GetRootPath(letter);

            return letter;
        }

        public ByteSize Size { get; set; }

        private static char GetFreeDriveLetter()
        {
            Log.Debug("Getting free drive letter");

            var drives = Enumerable.Range('C', 'Z').Select(i => (char)i);
            var usedDrives = DriveInfo.GetDrives().Select(x => char.ToUpper(x.Name[0]));

            var available = drives.Except(usedDrives);

            var driveLetter = available.First();

            Log.Verbose("Free drive letter={Letter}", driveLetter);

            return driveLetter;
        }

        public async Task SetGptType(PartitionType partitionType)
        {
            Log.Verbose("Setting new GPT partition type {Type} to {Partition}", partitionType, this);

            if (Equals(PartitionType, partitionType))
            {
                return;
            }

            using (var context = await GptContextFactory.Create(Disk.Number, FileAccess.ReadWrite, GptContext.DefaultBytesPerSector, GptContext.DefaultChunkSize))
            {
                var part = context.Find(Guid);
                part.PartitionType = partitionType;
            }

            await Disk.Refresh();

            Log.Verbose("New GPT type set correctly", partitionType, this);
        }

        public async Task<IVolume> GetVolume()
        {
            Log.Debug("Getting volume of {Partition}", this);

            var results = await PowerShellMixin.ExecuteCommand("Get-Volume",
                ("Partition", await this.GetPsPartition()));

            var result = results.FirstOrDefault()?.ImmediateBaseObject;

            if (result == null)
            {
                return null;
            }

            var driveLetter = (char?)result.GetPropertyValue("DriveLetter");
            var vol = new Volume(this)
            {
                Size = new ByteSize(Convert.ToUInt64(result.GetPropertyValue("Size"))),
                Label = (string)result.GetPropertyValue("FileSystemLabel"),
                Root = driveLetter != null ? PathExtensions.GetRootPath(driveLetter.Value) : null,
                FileSystemFormat = FileSystemFormat.FromString((string)result.GetPropertyValue("FileSystem")),
            };

            Log.Debug("Obtained {Volume}", vol);

            return vol;
        }

        public Task Resize(ByteSize newSize)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return $@"Partition '{Name ?? "Unnamed"}' - Guid: {Guid} in {Disk}. ";
        }

        protected bool Equals(IPartition other)
        {
            return Disk.Equals(other.Disk) && Guid.Equals(other.Guid);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != this.GetType())
            {
                return false;
            }

            return Equals((Partition) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Disk.GetHashCode() * 397) ^ Guid.GetHashCode();
            }
        }
    }
}