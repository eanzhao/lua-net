local from_string = assert(load(chunk, "=(string)", "b", { x = 41 }))()
local from_reader = assert(load(reader, "=(reader)", "b", { x = 42 }))()
x = 11
local from_loadfile = assert(loadfile(path, "b", { x = 7 }))()
local from_dofile = dofile(path)

return from_string, from_reader, from_loadfile, from_dofile
