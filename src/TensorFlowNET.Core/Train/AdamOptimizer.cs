﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tensorflow.Framework;
using static Tensorflow.Python;

namespace Tensorflow.Train
{
    /// <summary>
    /// Optimizer that implements the Adam algorithm.
    /// http://arxiv.org/abs/1412.6980
    /// </summary>
    public class AdamOptimizer : Optimizer
    {
        float _beta1;
        float _beta2;
        float _epsilon;
        Tensor _lr_t, _beta1_t, _beta2_t, _epsilon_t;

        public AdamOptimizer(float learning_rate, float beta1 = 0.9f, float beta2 = 0.999f, float epsilon = 1e-8f, bool use_locking = false, string name = "Adam")
            : base(learning_rate, use_locking, name)
        {
            _beta1 = beta1;
            _beta2 = beta2;
            _epsilon = epsilon;
        }

        public override Operation _apply_sparse(IndexedSlices grad, RefVariable var)
        {
            return _apply_sparse_shared(grad.values, var, grad.indices, (x, i, v) =>
            {
                return state_ops.scatter_add(x, i, v, use_locking: _use_locking);
            });
        }

        private Operation _apply_sparse_shared(Tensor grad, RefVariable var, Tensor indices, Func<RefVariable, Tensor, Tensor, Tensor> scatter_add)
        {
            var (beta1_power_v, beta2_power_v) = _get_beta_accumulators();
            Tensor beta1_power = math_ops.cast(beta1_power_v, var.dtype.as_base_dtype());
            Tensor beta2_power = math_ops.cast(beta2_power_v, var.dtype.as_base_dtype());
            var lr_t = math_ops.cast(_lr_t, var.dtype.as_base_dtype());
            var beta1_t = math_ops.cast(_beta1_t, var.dtype.as_base_dtype());
            var beta2_t = math_ops.cast(_beta2_t, var.dtype.as_base_dtype());
            var epsilon_t = math_ops.cast(_epsilon_t, var.dtype.as_base_dtype());
            var lr = (lr_t * math_ops.sqrt(1 - beta2_power) / (1 - beta1_power));
            var m = get_slot(var, "m");
            var m_scaled_g_values = grad * (1 - beta1_t);
            var m_t = state_ops.assign(m, m * beta1_t, use_locking: _use_locking);
            with(ops.control_dependencies(new[] { m_t }), delegate
            {
                m_t = scatter_add(m, indices, m_scaled_g_values);
            });

            var v = get_slot(var, "v");
            var v_scaled_g_values = (grad * grad) * (1 - beta2_t);
            var v_t = state_ops.assign(v, v * beta2_t, use_locking: _use_locking);
            with(ops.control_dependencies(new[] { v_t }), delegate
            {
                v_t = scatter_add(v, indices, v_scaled_g_values);
            });
            var v_sqrt = math_ops.sqrt(v_t);
            var var_update = state_ops.assign_sub(var, lr * m_t / (v_sqrt + epsilon_t), use_locking: _use_locking);
            return control_flow_ops.group(new[] { var_update, m_t, v_t });
        }

        protected override void _create_slots(RefVariable[] var_list)
        {
            var first_var = var_list.OrderBy(x => x.name).First();
            _create_non_slot_variable(initial_value: _beta1, name: "beta1_power", colocate_with: first_var);
            _create_non_slot_variable(initial_value: _beta2, name: "beta2_power", colocate_with: first_var);

            // Create slots for the first and second moments.
            foreach(var v in var_list)
            {
                _zeros_slot(v, "m", Name);
                _zeros_slot(v, "v", Name);
            }
        }

        private (RefVariable, RefVariable) _get_beta_accumulators()
        {
            ops.init_scope();
            var graph = ops.get_default_graph();
            return (_get_non_slot_variable("beta1_power", graph: graph),
                _get_non_slot_variable("beta2_power", graph: graph));
        }

        public override void _prepare()
        {
            //copied from GradientDescentOptimizer
            LearningRate = _call_if_callable(LearningRate);
            LearningRateTensor = ops.convert_to_tensor(LearningRate, name: "learning_rate");
        }
    }
}
