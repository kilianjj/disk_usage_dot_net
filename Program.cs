/**
Disk usage summary program
Author: Kilian Jakstis
*/

using System;
using System.IO;
using System.Diagnostics;

/**
Record containing fields for all necassary counts to keep track of
*/
public record DirectoryData(long Bytes, long ImageBytes, int FileCount, int FolderCount, int ImageCount){

    /**
    A function for adding up all the components of a list of directory records
    Used for adding up all the records of directories at the same level, so the totals can be returned in the recursive traversal
    param: List<DirectoryData> records - the list of records to add up
    return: record with totals
    */
    public static DirectoryData Accumulate(List<DirectoryData> records){
        long bytes = 0;
        long image_bytes = 0;
        int file_count = 0;
        int folder_count = 0;
        int image_count = 0;
        foreach (DirectoryData d in records){
            bytes += d.Bytes;
            image_bytes += d.ImageBytes;
            file_count += d.FileCount;
            folder_count += d.FolderCount;
            image_count += d.ImageCount;
        }
        return new(bytes, image_bytes, file_count, folder_count, image_count);
    }

    /**
    To string method for a record for printing program results
    param: int mode - mode that results came from 
    param: TimeSpan t - time elapsed during the traversal
    return: string message
    */
    public string ToString(int mode, TimeSpan t){
        // put no images found if applicable, or put the image count and image bytes
        string i = (ImageCount == 0) ? "no image files found in the directory" :
                                        $"{ImageCount.ToString("N0")} image files,  {ImageBytes.ToString("N0")} bytes";
        string m = (mode == 1)? "Sequential" : "Parallel";
        return $@"
{m} Calculated in: {t.TotalSeconds}s
{FolderCount.ToString("N0")} folders, {FileCount.ToString("N0")} files, {Bytes.ToString("N0")} bytes 
{i}";
    }
}

/**
Main program class
Has all of the directory traversing and arg parsing logic
*/
class Program{

    /// lock object for achieving mutual exclusion of files/folders and counts in parallel method
    private static readonly object documentLock = new();

    /// image file extensions
    private static readonly string[] Image_extensions = new string[]{
    ".jpg",
    ".jpeg",
    ".png",
    ".gif",
    ".bmp",
    ".tiff",
    ".tif",
    ".svg",
    ".ico"
};

    /**
    Sequential recursive directory traversal function
    param: string dir - path to directory
    return: a DirectoryData record containing all required counts
    */
    public static DirectoryData Sequential_func(string dir){
        // record data
        long bytes = 0;
        int file_count = 0;
        int folder_count = 0;
        int image_count = 0;
        long image_bytes = 0;
        // get the files, subdirectories
        string[] sub_dirs = Directory.GetDirectories(dir);
        string[] files = Directory.GetFiles(dir);
        // update counts
        file_count += files.Length;
        folder_count += sub_dirs.Length;
        foreach (string f in files){
            try {
                FileInfo file = new(f);
                string extension = file.Extension;
                long size = file.Length;
                bytes += size;
                if (Image_extensions.Contains(extension)){
                    image_count += 1;
                    image_bytes += size;
                }
            } catch {
                continue;
            }
        }
        // put data in record
        DirectoryData this_dir = new(bytes, image_bytes, file_count, folder_count, image_count);
        List<DirectoryData> dirs = new(){this_dir};
        // recurse to subdirectories
        foreach (string folder in sub_dirs){
            try {
                dirs.Add(Sequential_func(folder));
            } catch {
                continue;
            }
        }
        // add all directory records and return
        return DirectoryData.Accumulate(dirs);
    }

    /**
    Parallel recursive directory traversal function
    param: string dir - path to directory
    return: a DirectoryData record containing all required counts
    */
    public static DirectoryData Parallel_func(string dir){
        // record data
        long bytes = 0;
        int file_count = 0;
        int folder_count = 0;
        int image_count = 0;
        long image_bytes = 0;
        // get the files, subdirectories
        string[] sub_dirs = Directory.GetDirectories(dir);
        string[] files = Directory.GetFiles(dir);
        // update counts
        file_count += files.Length;
        folder_count += sub_dirs.Length;
        Parallel.ForEach(files, (f, state) => {
            try {
                /// lock to get exclusive file access
                lock (documentLock) {
                    FileInfo file = new(f);
                    string extension = file.Extension;
                    long size = file.Length;
                    bytes += size;
                    if (Image_extensions.Contains(extension)){
                        image_count += 1;
                        image_bytes += size;
                    }
                }
            } catch {
                // just catch the exception (such as no file access) and attempt to carry on
            }
        });
        // record data as a record
        DirectoryData this_dir = new(bytes, image_bytes, file_count, folder_count, image_count);
        List<DirectoryData> dirs = new(){this_dir};
        // recurse for all subdirectories
        Parallel.ForEach(sub_dirs, (d, state) => {
            try {
                // lock (documentLock){
                    dirs.Add(Parallel_func(d));
                // }
            } catch {
                // catch exception and carry on
            }
        });
        // add up all records and return
        return DirectoryData.Accumulate(dirs);
    }
    
    /**
    Print the program help message
    */
    static void Help_message(){
        Console.WriteLine(@"Usage: du [-s] [-d] [-b] <path>
Summarize disk usage of the set of FILEs, recursively for directories.

You MUST specify one of the parameters, -s, -d, or -b
-s       Run in single threaded mode
-d       Run in parallel mode (uses all available processors)
-b       Run in both single threaded and parallel mode.
         Runs parallel follow by sequential mode");
    }

    /**
    Check which option was passed on the commandline 
    */
    static int CheckOption(string o){
        return o switch
        {
            "-s" => 1,
            "-d" => 2,
            "-b" => 3,
            _ => 0,
        };
    }

    /**
    Start the stopwatch, run the appropriate directory traversal, stop the watch, print results
    param: string dir - directory path
    param: int mode - int representing whether to run in sequential, parallel, or both ways
    */
    static void Begin(string dir, int mode){
        // start the watch
        string print_me = "";
        Stopwatch stopwatch = new();
        stopwatch.Start();
        switch (mode){
            case 1:
                // sequential
                DirectoryData d = Sequential_func(dir);
                stopwatch.Stop();
                print_me += d.ToString(mode, stopwatch.Elapsed);
                Console.WriteLine($"Directory \'{dir}\':");
                Console.WriteLine(print_me);
                break;
            case 2:
                // parallel
                DirectoryData dd = Parallel_func(dir);
                stopwatch.Stop();
                print_me += dd.ToString(mode, stopwatch.Elapsed);
                Console.WriteLine($"Directory \'{dir}\':");
                Console.WriteLine(print_me);
                break;
            default:
                // both
                DirectoryData ddd = Parallel_func(dir);
                stopwatch.Stop();
                print_me += ddd.ToString(2, stopwatch.Elapsed);
                print_me += "\n";
                stopwatch.Start();
                ddd = Sequential_func(dir);
                stopwatch.Stop();
                print_me += ddd.ToString(1, stopwatch.Elapsed);
                Console.WriteLine($"Directory \'{dir}\':");
                Console.WriteLine(print_me);
                return;
        }
    }

    /**
    Check number of args, check directory is valid, check option is valid
    If valid, call the traversal method on that directory - otherwise print help message
    */
    static void Main(string[] args){
        if (args.Length != 2)
        {
            Help_message();
            return;
        }
        DirectoryInfo directoryInfo = new(@args[1]);
        int option = CheckOption(args[0]);
        if (directoryInfo.Exists && option > 0){
            Begin(directoryInfo.FullName, option);
        } else {
            Help_message();
        }
    }
}