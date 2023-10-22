
◊rroot"_tf_keras_layer*∑r{
  "name": "toy_resnet",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "class_name": "Functional",
  "config": {
    "name": "toy_resnet",
    "layers": [
      {
        "name": "img",
        "class_name": "InputLayer",
        "config": {
          "sparse": false,
          "ragged": false,
          "name": "img",
          "dtype": "float32",
          "batch_input_shape": {
            "class_name": "TensorShape",
            "items": [
              null,
              32,
              32,
              3
            ]
          }
        },
        "inbound_nodes": []
      },
      {
        "name": "conv2d",
        "class_name": "Conv2D",
        "config": {
          "filters": 32,
          "kernel_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "padding": "valid",
          "data_format": "channels_last",
          "dilation_rate": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "groups": 1,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "img",
            0,
            0
          ]
        ]
      },
      {
        "name": "conv2d_1",
        "class_name": "Conv2D",
        "config": {
          "filters": 64,
          "kernel_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "padding": "valid",
          "data_format": "channels_last",
          "dilation_rate": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "groups": 1,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "conv2d",
            0,
            0
          ]
        ]
      },
      {
        "name": "max_pooling2d",
        "class_name": "MaxPooling2D",
        "config": {
          "pool_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "padding": "valid",
          "data_format": "channels_last",
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "conv2d_1",
            0,
            0
          ]
        ]
      },
      {
        "name": "conv2d_2",
        "class_name": "Conv2D",
        "config": {
          "filters": 64,
          "kernel_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "padding": "same",
          "data_format": "channels_last",
          "dilation_rate": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "groups": 1,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "max_pooling2d",
            0,
            0
          ]
        ]
      },
      {
        "name": "conv2d_3",
        "class_name": "Conv2D",
        "config": {
          "filters": 64,
          "kernel_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "padding": "same",
          "data_format": "channels_last",
          "dilation_rate": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "groups": 1,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "conv2d_2",
            0,
            0
          ]
        ]
      },
      {
        "name": "add",
        "class_name": "Add",
        "config": {},
        "inbound_nodes": [
          [
            "conv2d_3",
            0,
            0
          ],
          [
            "max_pooling2d",
            0,
            0
          ]
        ]
      },
      {
        "name": "conv2d_4",
        "class_name": "Conv2D",
        "config": {
          "filters": 64,
          "kernel_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "padding": "same",
          "data_format": "channels_last",
          "dilation_rate": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "groups": 1,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "add",
            0,
            0
          ]
        ]
      },
      {
        "name": "conv2d_5",
        "class_name": "Conv2D",
        "config": {
          "filters": 64,
          "kernel_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "padding": "same",
          "data_format": "channels_last",
          "dilation_rate": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "groups": 1,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "conv2d_4",
            0,
            0
          ]
        ]
      },
      {
        "name": "add_1",
        "class_name": "Add",
        "config": {},
        "inbound_nodes": [
          [
            "conv2d_5",
            0,
            0
          ],
          [
            "add",
            0,
            0
          ]
        ]
      },
      {
        "name": "conv2d_6",
        "class_name": "Conv2D",
        "config": {
          "filters": 64,
          "kernel_size": {
            "class_name": "__tuple__",
            "items": [
              3,
              3
            ]
          },
          "strides": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "padding": "valid",
          "data_format": "channels_last",
          "dilation_rate": {
            "class_name": "__tuple__",
            "items": [
              1,
              1
            ]
          },
          "groups": 1,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "add_1",
            0,
            0
          ]
        ]
      },
      {
        "name": "global_average_pooling2d",
        "class_name": "GlobalAveragePooling2D",
        "config": {
          "pool_size": null,
          "strides": null,
          "padding": "valid",
          "data_format": "channels_last",
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "conv2d_6",
            0,
            0
          ]
        ]
      },
      {
        "name": "dense",
        "class_name": "Dense",
        "config": {
          "units": 256,
          "activation": "relu",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "global_average_pooling2d",
            0,
            0
          ]
        ]
      },
      {
        "name": "dropout",
        "class_name": "Dropout",
        "config": {
          "rate": 0.5,
          "noise_shape": null,
          "seed": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "dense",
            0,
            0
          ]
        ]
      },
      {
        "name": "dense_1",
        "class_name": "Dense",
        "config": {
          "units": 10,
          "activation": "linear",
          "use_bias": true,
          "kernel_initializer": {
            "class_name": "GlorotUniform",
            "config": {
              "seed": null
            }
          },
          "bias_initializer": {
            "class_name": "Zeros",
            "config": {}
          },
          "kernel_regularizer": null,
          "bias_regularizer": null,
          "kernel_constraint": null,
          "bias_constraint": null,
          "name": null,
          "dtype": "float32",
          "trainable": true
        },
        "inbound_nodes": [
          [
            "dropout",
            0,
            0
          ]
        ]
      }
    ],
    "input_layers": [
      [
        "img",
        0,
        0
      ]
    ],
    "output_layers": [
      [
        "dense_1",
        0,
        0
      ]
    ]
  },
  "shared_object_id": 1,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      32,
      32,
      3
    ]
  }
}2
‡root.layer-0"_tf_keras_input_layer*∞{"class_name":"InputLayer","name":"img","dtype":"float32","sparse":false,"ragged":false,"batch_input_shape":{"class_name":"TensorShape","items":[null,32,32,3]},"config":{"sparse":false,"ragged":false,"name":"img","dtype":"float32","batch_input_shape":{"class_name":"TensorShape","items":[null,32,32,3]}}}2
§root.layer_with_weights-0"_tf_keras_layer*Ì{
  "name": "conv2d",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 4,
      "MaxNdim": null,
      "Axes": {
        "-1": 3
      }
    },
    "shared_object_id": 2
  },
  "class_name": "Conv2D",
  "config": {
    "filters": 32,
    "kernel_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "padding": "valid",
    "data_format": "channels_last",
    "dilation_rate": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "groups": 1,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 3,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      32,
      32,
      3
    ]
  }
}2
®root.layer_with_weights-1"_tf_keras_layer*Ò{
  "name": "conv2d_1",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 4,
      "MaxNdim": null,
      "Axes": {
        "-1": 32
      }
    },
    "shared_object_id": 4
  },
  "class_name": "Conv2D",
  "config": {
    "filters": 64,
    "kernel_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "padding": "valid",
    "data_format": "channels_last",
    "dilation_rate": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "groups": 1,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 5,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      30,
      30,
      32
    ]
  }
}2
Åroot.layer-3"_tf_keras_layer*◊{
  "name": "max_pooling2d",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "class_name": "MaxPooling2D",
  "config": {
    "pool_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "padding": "valid",
    "data_format": "channels_last",
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 6,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      28,
      28,
      64
    ]
  }
}2
•root.layer_with_weights-2"_tf_keras_layer*Ó{
  "name": "conv2d_2",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 4,
      "MaxNdim": null,
      "Axes": {
        "-1": 64
      }
    },
    "shared_object_id": 7
  },
  "class_name": "Conv2D",
  "config": {
    "filters": 64,
    "kernel_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "padding": "same",
    "data_format": "channels_last",
    "dilation_rate": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "groups": 1,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 8,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      9,
      9,
      64
    ]
  }
}2
¶root.layer_with_weights-3"_tf_keras_layer*Ô{
  "name": "conv2d_3",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 4,
      "MaxNdim": null,
      "Axes": {
        "-1": 64
      }
    },
    "shared_object_id": 9
  },
  "class_name": "Conv2D",
  "config": {
    "filters": 64,
    "kernel_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "padding": "same",
    "data_format": "channels_last",
    "dilation_rate": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "groups": 1,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 10,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      9,
      9,
      64
    ]
  }
}2
äroot.layer-6"_tf_keras_layer*‡{
  "name": "add",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "class_name": "Add",
  "config": {},
  "shared_object_id": 11,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      9,
      9,
      64
    ]
  }
}2
ßroot.layer_with_weights-4"_tf_keras_layer*{
  "name": "conv2d_4",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 4,
      "MaxNdim": null,
      "Axes": {
        "-1": 64
      }
    },
    "shared_object_id": 12
  },
  "class_name": "Conv2D",
  "config": {
    "filters": 64,
    "kernel_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "padding": "same",
    "data_format": "channels_last",
    "dilation_rate": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "groups": 1,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 13,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      9,
      9,
      64
    ]
  }
}2
ß	root.layer_with_weights-5"_tf_keras_layer*{
  "name": "conv2d_5",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 4,
      "MaxNdim": null,
      "Axes": {
        "-1": 64
      }
    },
    "shared_object_id": 14
  },
  "class_name": "Conv2D",
  "config": {
    "filters": 64,
    "kernel_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "padding": "same",
    "data_format": "channels_last",
    "dilation_rate": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "groups": 1,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 15,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      9,
      9,
      64
    ]
  }
}2
å
root.layer-9"_tf_keras_layer*‚{
  "name": "add_1",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "class_name": "Add",
  "config": {},
  "shared_object_id": 16,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      9,
      9,
      64
    ]
  }
}2
®root.layer_with_weights-6"_tf_keras_layer*Ò{
  "name": "conv2d_6",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 4,
      "MaxNdim": null,
      "Axes": {
        "-1": 64
      }
    },
    "shared_object_id": 17
  },
  "class_name": "Conv2D",
  "config": {
    "filters": 64,
    "kernel_size": {
      "class_name": "__tuple__",
      "items": [
        3,
        3
      ]
    },
    "strides": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "padding": "valid",
    "data_format": "channels_last",
    "dilation_rate": {
      "class_name": "__tuple__",
      "items": [
        1,
        1
      ]
    },
    "groups": 1,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 18,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      9,
      9,
      64
    ]
  }
}2
Êroot.layer-11"_tf_keras_layer*ª{
  "name": "global_average_pooling2d",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "class_name": "GlobalAveragePooling2D",
  "config": {
    "pool_size": null,
    "strides": null,
    "padding": "valid",
    "data_format": "channels_last",
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 19,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      7,
      7,
      64
    ]
  }
}2
Îroot.layer_with_weights-7"_tf_keras_layer*¥{
  "name": "dense",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 2,
      "MaxNdim": null,
      "Axes": {
        "-1": 64
      }
    },
    "shared_object_id": 20
  },
  "class_name": "Dense",
  "config": {
    "units": 256,
    "activation": "relu",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 21,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      64
    ]
  }
}2
Üroot.layer-13"_tf_keras_layer*€{
  "name": "dropout",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "class_name": "Dropout",
  "config": {
    "rate": 0.5,
    "noise_shape": null,
    "seed": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 22,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      256
    ]
  }
}2
root.layer_with_weights-8"_tf_keras_layer*π{
  "name": "dense_1",
  "trainable": true,
  "expects_training_arg": false,
  "dtype": "float32",
  "batch_input_shape": null,
  "autocast": false,
  "input_spec": {
    "class_name": "InputSpec",
    "config": {
      "DType": null,
      "Shape": null,
      "Ndim": null,
      "MinNdim": 2,
      "MaxNdim": null,
      "Axes": {
        "-1": 256
      }
    },
    "shared_object_id": 23
  },
  "class_name": "Dense",
  "config": {
    "units": 10,
    "activation": "linear",
    "use_bias": true,
    "kernel_initializer": {
      "class_name": "GlorotUniform",
      "config": {
        "seed": null
      }
    },
    "bias_initializer": {
      "class_name": "Zeros",
      "config": {}
    },
    "kernel_regularizer": null,
    "bias_regularizer": null,
    "kernel_constraint": null,
    "bias_constraint": null,
    "name": null,
    "dtype": "float32",
    "trainable": true
  },
  "shared_object_id": 24,
  "build_input_shape": {
    "class_name": "TensorShape",
    "items": [
      null,
      256
    ]
  }
}2