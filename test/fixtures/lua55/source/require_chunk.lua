package.path = module_path

preload_hits = 0
package.preload.pre_mod = function(name, loader)
    preload_hits = preload_hits + 1
    return {
        tag = name,
        loader = loader
    }
end

local pre_first, pre_loader = require("pre_mod")
local pre_second = require("pre_mod")

payload = 77
file_hits = 0
local file_first, file_loader = require("require_file_chunk")
local file_second = require("require_file_chunk")

true_hits = 0
package.preload.pre_true = function()
    true_hits = true_hits + 1
end

local true_first, true_loader = require("pre_true")
local true_second = require("pre_true")

return
    preload_hits,
    pre_first == pre_second,
    pre_first.tag,
    pre_first.loader,
    pre_loader,
    file_hits,
    file_first,
    file_second,
    file_loader,
    true_hits,
    true_first,
    true_second,
    true_loader
